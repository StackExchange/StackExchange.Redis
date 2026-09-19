using System.Threading;

namespace StackExchange.Redis
{
    /// <summary>What the client knows about whether it is talking to a cluster.</summary>
    /// <remarks>
    /// <b>Three states, not two</b>, because "we have not found out yet" behaves differently from both
    /// answers: see <see cref="RespTopology"/>.
    /// </remarks>
    internal enum RespClusterState
    {
        /// <summary>Nobody has connected yet, so nobody knows.</summary>
        Unknown = 0,

        /// <summary>Known not to be a cluster; slots mean nothing and cost nothing.</summary>
        No = 1,

        /// <summary>Known to be a cluster; slots decide routing.</summary>
        Yes = 2,
    }

    /// <summary>
    /// What the client currently believes about the shape of the server it is talking to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A mutable cell rather than a value, because the answer is not known when it is first needed.</b>
    /// A multiplexer can be constructed - and <c>GetDatabase()</c> called, and a context built and
    /// memoised - while still disconnected, at which point nobody knows whether this is a cluster. Baking
    /// the answer into the context at that moment means a deployment that turns out to be a cluster is
    /// routed as if it were not, permanently, because the context is cached for the life of the
    /// multiplexer.
    /// </para>
    /// <para>
    /// <b>And three states rather than two, which is the part that is easy to get wrong.</b> With a plain
    /// bool defaulting to "not a cluster", a request rendered before the answer arrived carries no slot -
    /// so when the answer does arrive, that request cannot be routed and has to be sent somewhere
    /// arbitrary and corrected by a redirect. <see cref="RespClusterState.Unknown"/> instead computes
    /// slots <i>speculatively</i>: the cost is a hash per key during the brief window before the first
    /// connection reports back, and in exchange there is no window in which a request exists that cannot
    /// be routed.
    /// </para>
    /// <para>
    /// Note the asymmetry between the two questions, which is why there are two properties rather than
    /// one. <see cref="NeedsSlots"/> is "might this matter?" and is true while unknown, because computing
    /// a slot nobody needs is merely wasted work. <see cref="RoutesBySlot"/> is "does this decide where
    /// the command goes?" and is false while unknown, because acting on a guess is not wasted work, it is
    /// wrong.
    /// </para>
    /// <para>
    /// <b>Read on the hot path</b> - every key written to a request consults <see cref="NeedsSlots"/> -
    /// so it is one volatile int and no indirection beyond that.
    /// </para>
    /// <para>
    /// <b>The alternative, and why it is not what this does yet.</b> The simpler design is to compute no
    /// slots while unknown, route those commands anywhere, and let <c>-MOVED</c> correct them: no
    /// speculative hashing, no third state. That is very likely the right end state - redirect handling
    /// is needed <i>regardless</i>, because a reshard moves slots under a running client and no amount of
    /// care at connection time helps with that. But the new core has no redirect handling at all today,
    /// so "let MOVED do the thing" is a promise rather than a mechanism: those commands would surface the
    /// redirect to the caller as an error. The speculative hash is how this window is made correct
    /// <i>before</i> that exists, and <see cref="RespClusterState.Unknown"/> is deletable once it does.
    /// </para>
    /// <para>
    /// <b>And the cost of a redirect is not just a round trip - it is ordering.</b> Within one connection
    /// order is preserved by construction: one FIFO queue, replies matched in sequence. A redirected
    /// command leaves that queue and joins a different one, so its position relative to everything issued
    /// after it is lost. A caller that issues <c>INCR k</c> then <c>GET k</c> can have the <c>GET</c> land
    /// on the owning node and complete while the <c>INCR</c> is still being bounced to it - and read the
    /// value from before its own write. That is not a slow path, it is a wrong answer, and it is the
    /// strongest argument for knowing the slot before the command is sent rather than after.
    /// </para>
    /// </remarks>
    internal sealed class RespTopology
    {
        private int _state;

        /// <summary>Create a topology cell, optionally with what is already known.</summary>
        /// <param name="state">What is known now; <see cref="RespClusterState.Unknown"/> if nothing is.</param>
        internal RespTopology(RespClusterState state = RespClusterState.Unknown) => _state = (int)state;

        /// <summary>Create a topology cell from a known server type.</summary>
        /// <param name="serverType">The server type.</param>
        internal RespTopology(ServerType serverType)
            => _state = (int)(serverType == ServerType.Cluster ? RespClusterState.Yes : RespClusterState.No);

        /// <summary>What is currently known.</summary>
        internal RespClusterState State
        {
            get => (RespClusterState)Volatile.Read(ref _state);
            set => Volatile.Write(ref _state, (int)value);
        }

        /// <summary>Record what a connection reported.</summary>
        /// <param name="serverType">The server type the handshake or configuration discovered.</param>
        /// <remarks>
        /// <para>
        /// <b>The transition this is designed around is <see cref="RespClusterState.Unknown"/> to known,
        /// and only that one.</b> A deployment that genuinely changes between cluster and not is a
        /// reconfiguration, and reconfigurations are expected to be disruptive - in-flight commands may be
        /// routed on the old answer, get redirected or fail, and be retried. That is a bump, and bumps are
        /// acceptable there; designing the hot path around making them seamless would be paying, on every
        /// command forever, for something that happens approximately never.
        /// </para>
        /// <para>
        /// So this is written to be called repeatedly with the same answer, and it is not an error to
        /// change it - but nothing downstream promises to make a change graceful.
        /// </para>
        /// </remarks>
        internal void OnServerType(ServerType serverType)
            => State = serverType == ServerType.Cluster ? RespClusterState.Yes : RespClusterState.No;

        /// <summary>
        /// Whether a key's hash slot should be computed - true unless we know it cannot matter.
        /// </summary>
        /// <remarks>
        /// Speculative while unknown. A slot computed and never used costs a hash; a slot NOT computed and
        /// then needed cannot be recovered, because by then the request is rendered and the key bytes have
        /// been handed on.
        /// </remarks>
        internal bool NeedsSlots => (RespClusterState)Volatile.Read(ref _state) != RespClusterState.No;

        /// <summary>Whether the slot decides which endpoint serves the command.</summary>
        /// <remarks>False while unknown: routing on a guess is wrong, not merely wasteful.</remarks>
        internal bool RoutesBySlot => (RespClusterState)Volatile.Read(ref _state) == RespClusterState.Yes;

        /// <summary>The server type, as far as anyone knows.</summary>
        internal ServerType ServerType => RoutesBySlot ? ServerType.Cluster : ServerType.Standalone;
    }
}
