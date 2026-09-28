using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
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

    /// <summary>Which side of a replication pair an endpoint is, as far as this core has been told.</summary>
    /// <remarks>
    /// <b>Three states again, and for the same reason <see cref="RespClusterState"/> has three.</b> "We
    /// have not asked" is not "it is a primary": a <see cref="CommandFlags.DemandReplica"/> answered from
    /// a default of <see cref="Primary"/> would silently send a read to the wrong side, where answered
    /// from <see cref="Unknown"/> it defers to whoever does know.
    /// </remarks>
    internal enum RespEndpointRole
    {
        /// <summary>Nobody has said.</summary>
        Unknown = 0,

        /// <summary>Accepts writes; the owner of its slots.</summary>
        Primary = 1,

        /// <summary>Replicates a primary; may serve reads.</summary>
        Replica = 2,
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
    /// <b>The redirect cost is ordering, but only at the boundary.</b> The blunt version of this claim -
    /// "a redirect loses ordering" - is wrong, and it is worth writing down why. If a redirect is handled
    /// <i>in the IO loop</i>, in reply order, then commands that were redirected <i>together</i> keep
    /// their order: they were sent to the wrong node in order, that node answers <c>-MOVED</c> to each in
    /// that same order, and re-enqueueing as the replies arrive puts them on the new connection in the
    /// caller's original order. (Resubmitting from arbitrary threads would lose it even here, so "in the
    /// IO loop" is doing real work in that sentence.)
    /// </para>
    /// <para>
    /// What genuinely inverts is the <b>mixed</b> case, which is exactly the discovery boundary: one
    /// command issued while the topology was unknown takes the slow path - wrong node, redirect, new
    /// connection - while the next, issued a moment later with the answer in hand, goes straight to the
    /// owner. A caller issuing <c>INCR k</c> then <c>GET k</c> across that boundary can have the
    /// <c>GET</c> complete while the <c>INCR</c> is still in flight, and read the value from before its
    /// own write.
    /// </para>
    /// <para>
    /// <b>What keeps this design out of that case is an invariant, and it should be stated rather than
    /// relied on quietly:</b> <see cref="RespClusterState.Unknown"/> means no connection has reported
    /// yet, which in practice means there is no connection - so commands issued then are <i>backlogged,
    /// not sent</i>, and drain in arrival order once the topology is known and their speculative slots
    /// become meaningful. No redirect, no inversion. The invariant breaks if a connection is ever brought
    /// up and used while its server type is still unset, so whoever owns an endpoint must set the topology
    /// as part of bringing a connection up, <b>before</b> draining the backlog.
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

        // ---- the slot map -------------------------------------------------------------------------------

        /// <summary>Who serves one contiguous run of slots.</summary>
        /// <remarks>
        /// <b>One instance per range, referenced from every slot in it</b>, so a three-primary cluster is
        /// three objects behind 16,384 references rather than 16,384 objects - and so a read is an index
        /// and a field, with no search and no lock.
        /// <para>
        /// Immutable, which is what lets the whole answer for a slot - primary AND its replicas - be
        /// swapped with one reference write. A mutable pair would let a reader see a new primary beside
        /// the old primary's replicas, which is a routing table nobody ever published.
        /// </para>
        /// </remarks>
        internal sealed class SlotOwners
        {
            internal SlotOwners(EndPoint primary, EndPoint[]? replicas)
            {
                Primary = primary;
                Replicas = replicas is { Length: > 0 } ? replicas : Array.Empty<EndPoint>();
            }

            /// <summary>The endpoint that accepts writes for these slots.</summary>
            internal EndPoint Primary { get; }

            /// <summary>The endpoints replicating them; empty, never null.</summary>
            internal EndPoint[] Replicas { get; }
        }

        /// <summary>Who owns each slot, or null while nothing has said.</summary>
        /// <remarks>
        /// <b>This core's own map, and the point of it is that it is its own.</b> Routing used to resolve a
        /// slot through <c>ServerSelectionStrategy</c>, whose map is a <c>ServerEndPoint[]</c> filled from a
        /// <c>CLUSTER NODES</c> that the SHIPPED core issued during its auto-configure - so this core could
        /// not route until the other one had connected, and both had to stay up for every command. That one
        /// fact is behind every symptom in section 9a.
        /// <para>
        /// Endpoints rather than executors: an executor is created on demand and may be retired, while the
        /// answer "slot 42 lives at 127.0.0.1:7001" outlives both. Allocated on first write, because a
        /// standalone deployment never needs 16,384 of anything.
        /// </para>
        /// </remarks>
        private SlotOwners?[]? _slots;

        /// <summary>Whether this core has a slot map of its own yet.</summary>
        /// <remarks>
        /// The caller falls back to the shipped selector while this is false, which is what lets the map be
        /// adopted before it is complete: an empty map routes exactly as before rather than routing wrongly.
        /// </remarks>
        internal bool HasSlotMap => Volatile.Read(ref _slots) is not null;

        /// <summary>Who serves a slot, or null if this core has not been told.</summary>
        /// <param name="slot">The hash slot.</param>
        internal SlotOwners? Owners(int slot)
        {
            var map = Volatile.Read(ref _slots);
            return map is null || (uint)slot >= (uint)map.Length ? null : Volatile.Read(ref map[slot]);
        }

        /// <summary>The endpoint that owns a slot, or null if this core has not been told.</summary>
        /// <param name="slot">The hash slot.</param>
        internal EndPoint? SlotOwner(int slot) => Owners(slot)?.Primary;

        /// <summary>Record who serves an inclusive range of slots.</summary>
        /// <param name="from">First slot, inclusive.</param>
        /// <param name="to">Last slot, inclusive.</param>
        /// <param name="primary">The endpoint accepting writes for them.</param>
        /// <param name="replicas">The endpoints replicating it, if any.</param>
        /// <remarks>
        /// Written per slot rather than as ranges because reads are on the command path and must be an
        /// index, not a search; 16,384 references is 128KB once per deployment.
        /// <para>
        /// The roles fall out of the same reply, and are recorded here rather than by the caller so that
        /// "what the map says" and "what role an endpoint has" cannot disagree - a keyless
        /// <c>PreferReplica</c> and a keyed one would otherwise be answered from two sources.
        /// </para>
        /// </remarks>
        internal void SetSlotRange(int from, int to, EndPoint primary, EndPoint[]? replicas = null)
        {
            if (primary is null || from < 0 || to < from || to >= RedisClusterSlotCount) return;

            var map = Volatile.Read(ref _slots);
            if (map is null)
            {
                var created = new SlotOwners?[RedisClusterSlotCount];
                map = Interlocked.CompareExchange(ref _slots, created, null) ?? created;
            }

            var owners = new SlotOwners(primary, replicas);
            for (var slot = from; slot <= to; slot++) Volatile.Write(ref map[slot], owners);

            OnRole(primary, RespEndpointRole.Primary);
            foreach (var replica in owners.Replicas) OnRole(replica, RespEndpointRole.Replica);
        }

        /// <summary>Move a single slot, as a <c>MOVED</c> says to.</summary>
        /// <param name="slot">The slot that moved.</param>
        /// <param name="endpoint">Where the server says it went.</param>
        /// <remarks>
        /// <b>Only when a map already exists.</b> One <c>MOVED</c> is evidence about one slot, not grounds
        /// to invent a map in which every other slot is unknown - that would flip routing from "ask the
        /// selector" to "ask a map that knows almost nothing", which is worse than not having one.
        /// <para>
        /// The new owner arrives with no replicas, and inventing some would be worse than having none: a
        /// <c>MOVED</c> names one endpoint, and carrying the OLD range's replicas across would point
        /// replica reads at nodes that no longer replicate this slot. They come back with the next
        /// <c>CLUSTER SLOTS</c>; until then a replica preference resolves to the primary, which is what
        /// "prefer" means.
        /// </para>
        /// </remarks>
        internal void OnSlotMoved(int slot, EndPoint endpoint)
        {
            var map = Volatile.Read(ref _slots);
            if (map is not null && (uint)slot < (uint)map.Length && endpoint is not null)
            {
                Volatile.Write(ref map[slot], new SlotOwners(endpoint, null));
                OnRole(endpoint, RespEndpointRole.Primary);
            }
        }

        // ---- roles --------------------------------------------------------------------------------------

        /// <summary>What role each endpoint plays, as far as anyone has said.</summary>
        /// <remarks>
        /// <b>Separate from the slot map because the question outlives it.</b> A standalone primary/replica
        /// pair has no slots at all and still has to answer <see cref="CommandFlags.PreferReplica"/>, and a
        /// keyless command in a cluster has no slot to look the answer up by.
        /// </remarks>
        private readonly ConcurrentDictionary<EndPoint, RespEndpointRole> _roles = new();

        /// <summary>The endpoints known to be replicas, for choosing one without a slot.</summary>
        /// <remarks>
        /// A snapshot array rather than a filter over <see cref="_roles"/>: the read is on the routing path
        /// and wants an index, while the write happens once per endpoint per discovery. Replaced wholesale
        /// so a reader either sees the old set or the new one, never a half-built one.
        /// </remarks>
        private EndPoint[] _replicaSet = Array.Empty<EndPoint>();

        /// <summary>Whether it is worth asking a standalone server which side of a pair it is.</summary>
        /// <remarks>
        /// <b>A cost guard, not a correctness one.</b> The cluster map reports roles for free, inside a
        /// reply that is being read anyway; a standalone server needs its own <c>INFO REPLICATION</c>, one
        /// per connection. With a single endpoint configured there is nothing to prefer a replica OVER, so
        /// that round trip buys a fact routing cannot act on - and it would be paid on every connection.
        /// </remarks>
        internal bool WantsRoles { get; set; }

        /// <summary>Whether anything is known about any endpoint's role.</summary>
        /// <remarks>False means "defer to whoever does know" rather than "there are no replicas".</remarks>
        internal bool HasRoles => !_roles.IsEmpty;

        /// <summary>What role an endpoint plays, as far as anyone has said.</summary>
        /// <param name="endpoint">The endpoint.</param>
        internal RespEndpointRole RoleOf(EndPoint endpoint)
            => endpoint is not null && _roles.TryGetValue(endpoint, out var role) ? role : RespEndpointRole.Unknown;

        /// <summary>The endpoints currently believed to be replicas.</summary>
        internal EndPoint[] Replicas => Volatile.Read(ref _replicaSet);

        /// <summary>Record what role an endpoint plays.</summary>
        /// <param name="endpoint">The endpoint.</param>
        /// <param name="role">What it turned out to be.</param>
        /// <remarks>
        /// Idempotent, and cheap when it is: the snapshot is only rebuilt when the answer actually changed,
        /// so re-running a handshake against an unchanged cluster allocates nothing.
        /// </remarks>
        internal void OnRole(EndPoint endpoint, RespEndpointRole role)
        {
            if (endpoint is null || role == RespEndpointRole.Unknown) return;

            if (_roles.TryGetValue(endpoint, out var existing) && existing == role) return;
            _roles[endpoint] = role;

            lock (_roles)
            {
                var replicas = new List<EndPoint>();
                foreach (var pair in _roles)
                {
                    if (pair.Value == RespEndpointRole.Replica) replicas.Add(pair.Key);
                }

                Volatile.Write(ref _replicaSet, replicas.Count == 0 ? Array.Empty<EndPoint>() : replicas.ToArray());
            }
        }

        /// <summary>The slot count a cluster deployment uses; mirrors the shipped constant.</summary>
        private const int RedisClusterSlotCount = 16384;
    }
}
