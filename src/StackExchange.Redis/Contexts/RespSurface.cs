using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using RESPite;
using RESPite.Messages;
using StackExchange.Redis.Protocol;

namespace StackExchange.Redis
{
    /// <summary>
    /// EXPERIMENTAL SPIKE. The root of the context-based surface: one member, from which everything else
    /// hangs as extension members.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The point of having exactly one member is that it is the <b>last</b> addition to an interface. Once a
    /// context is reachable, new commands - ours and other libraries' - are extension members over it, and
    /// break nobody. See design notes section 9.4.
    /// </para>
    /// <para>
    /// <b>This carries the shared plumbing; the SEMANTIC context lives on the derived interfaces.</b>
    /// <see cref="IRespKeyspaceTarget.Context"/> is a <see cref="RespDatabaseContext"/> and
    /// <see cref="IRespServerTarget.Context"/> is a <see cref="RespServerContext"/>, which is what decides
    /// whether <c>Strings</c> or <c>Keyspace</c> is on offer. <see cref="Context"/> here is what both of
    /// them wrap, and the derived interfaces hide it with <c>new</c> - so <c>db.Context</c> is the typed
    /// one and only a caller who has asked for an <see cref="IRespTarget"/> sees the plain one.
    /// </para>
    /// <para>
    /// <see cref="Context"/> returns <b>by value</b>. A <c>ref readonly</c> would save a copy of roughly
    /// four registers, and cost the ability to use the result in an <c>async</c> method - which is the only
    /// kind of method this surface has.
    /// </para>
    /// </remarks>
    public interface IRespTarget
    {
        /// <summary>The shared plumbing every context wraps: command map, key prefix, services, executor.</summary>
        /// <remarks>
        /// <b>Called <c>Context</c>, and hidden by the derived interfaces.</b> It was <c>Raw</c>, which put
        /// the word on every target that carries one - so <c>db.Raw</c> read as an ordinary part of the
        /// surface when it is the opposite. Naming it <c>Context</c> and letting
        /// <see cref="IRespKeyspaceTarget"/> and <see cref="IRespServerTarget"/> hide it with <c>new</c>
        /// means the typed context is what you get by default, and the plain one only when you have gone
        /// out of your way to hold an <see cref="IRespTarget"/>.
        /// </remarks>
        RespContext Context { get; }
    }

    /// <summary>
    /// EXPERIMENTAL SPIKE. A target whose commands are routed by <b>key</b>: a database, a batch, a
    /// transaction.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The keyspace groups - <c>Strings</c>, <c>Hashes</c>, <c>Keys</c> and the rest - bind here rather
    /// than to <see cref="IRespTarget"/>, so that resolving them is what decides whether they make sense.
    /// They used to bind to <see cref="IRespTarget"/>, which <c>IServer</c> and <c>ISubscriber</c> also
    /// carry, so <c>server.Strings.GetAsync(key)</c> compiled - an offer of something an <c>IServer</c> has no
    /// business doing, discovered by exactly the tab-completion that is meant to be the point.
    /// </para>
    /// <para>
    /// Note what this does <i>not</i> do: it does not carry a different executor. Routing already differs
    /// correctly by inheritance - a batch's context queues because its executor's target is the batch, and
    /// a server's pins to one endpoint because <c>RedisServer.ExecuteAsync</c> injects it. The split is
    /// about which commands are <b>offered</b>, which is a separate question from where they go.
    /// </para>
    /// </remarks>
    public interface IRespKeyspaceTarget : IRespTarget
    {
        /// <summary>The keyspace context: the entry point to the database command groups.</summary>
        /// <remarks>
        /// <c>new</c>, hiding <see cref="IRespTarget.Context"/>: a target that knows it is key-routed
        /// answers with the context that says so, and the plain one stays reachable through the base
        /// interface for the code that genuinely wants it.
        /// </remarks>
        new RespDatabaseContext Context { get; }
    }

    /// <summary>
    /// EXPERIMENTAL SPIKE. A target whose commands are pinned to <b>one endpoint</b>: a server.
    /// </summary>
    /// <remarks>
    /// The counterpart to <see cref="IRespKeyspaceTarget"/>, and the reason <c>IServer</c> keeps a context
    /// at all now that the keyspace groups have moved off it: server-scoped groups will bind here, and a
    /// third party's can too.
    /// <para>
    /// A few group <i>names</i> will end up on both, with different members - <c>Keys</c> is key-routed for
    /// <c>DEL</c> and server-scoped for <c>KEYS</c>/<c>SCAN</c>, and <c>Scripts</c> divides the same way
    /// between <c>EVALSHA</c> and <c>SCRIPT LOAD</c>. That is the line <c>IDatabase</c> and <c>IServer</c>
    /// already draw, and it wants drawing deliberately rather than by whichever group is written first.
    /// </para>
    /// </remarks>
    public interface IRespServerTarget : IRespTarget
    {
        /// <summary>The server context: the entry point to the server-scoped command groups.</summary>
        /// <remarks><inheritdoc cref="IRespKeyspaceTarget.Context" path="/remarks"/></remarks>
        new RespServerContext Context { get; }
    }

    /// <summary>
    /// EXPERIMENTAL SPIKE. The command surface, as extension members.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the shape the whole design exists to enable: <c>ctx.Strings.SetAsync(key, value)</c> reads like a
    /// built-in method, groups the surface the way Redis documents itself, and is reachable by any library -
    /// including one that is not this one - without a wrapper interface or a forked surface.
    /// </para>
    /// <para>
    /// One partial file per command group - <c>RespSurface.Strings.cs</c>, and so on - matching how Redis
    /// documents itself, and how <c>RedisDatabase</c>'s ~6k lines would have liked to be split.
    /// </para>
    /// </remarks>
    // HOW A COMMAND SAYS HOW SAFE IT IS TO REPLAY - the house rule for every group on this surface.
    //
    // The retry category comes from CommandFlagsExtensions.WithDefaultCategory: the same per-command table
    // the MessageWriter path uses, rather than a constant named at each call site. Two writers agreeing on
    // the bytes and disagreeing on whether a command is safe to replay is the kind of divergence nothing
    // would catch, so the table is the single source of truth.
    //
    // A command whose ARGUMENTS change the answer raises it EXPLICITLY, with WithRetryCategory, before
    // falling through to the table - SORT with STORE, GETEX with a TTL, SET under NX, SCAN past the first
    // cursor. There are 18 such sites, and they are the whole reason the table can afford to be keyed on
    // the command alone: it says "raised to a write where we can see the args", and these are where they
    // are seen. WithRetryCategory is caller-wins, so this composes in either order and never overrides a
    // category the caller chose.
    //
    // Which is also why spelling the category at all ~250 sites was considered and REJECTED: it would be
    // ~240 restatements of the table plus the 18 refinements that already exist, and the restatements are
    // pure drift risk against the path that still reads the table. See the queue.
    //
    // WithRetryCategory stays public for surfaces outside this assembly, which cannot see the table.
    //
    // RS0026 warns about overloads that carry optional parameters, because adding one later can make an
    // existing call ambiguous. That hazard cannot arise here, and saying so once beats a pragma per
    // command: every member of this class is an extension method whose FIRST parameter is a group type -
    // RespStrings, RespHashes, RespSets, ... - so two members sharing a name are only ever candidates for
    // the same call when their receivers are the same group, and within a group the overloads differ in a
    // parameter that has no default (a span versus a single value, a long versus a double). The names
    // repeat across groups on purpose: ctx.Strings.Length and ctx.Sets.Length are the same word because
    // they are the same idea, which is the entire argument for grouping.
    [SuppressMessage("ApiDesign", "RS0026:Do not add multiple overloads with optional parameters", Justification = "Extension members on distinct group types; see the comment above")]
    public static partial class RespSurface
    {
        /// <summary>The shared plumbing behind a target, whatever kind of target it is.</summary>
        /// <remarks>
        /// <para>
        /// <b>Internal, and a synonym for <see cref="IRespTarget.Context"/>.</b> It exists to say which
        /// <c>Context</c> is meant at the call sites that want the plain one - a wrapper reaching into the
        /// thing it wraps, say - where writing <c>((IRespTarget)inner).Context</c> would be noise and
        /// <c>inner.Context</c> would silently pick the typed one.
        /// </para>
        /// <para>
        /// <b>Not public</b>, deliberately. Making it public would put <c>Raw</c> back on every target and
        /// on the typed contexts, which is what moving to <c>Context</c> plus an explicit cast was for.
        /// Outside this assembly the base interface is the way to ask.
        /// </para>
        /// </remarks>
        extension<TTarget>(TTarget target) where TTarget : IRespTarget
        {
            internal RespContext Raw => target.Context;

            /// <summary>
            /// <c>PING</c>; completes when the server has answered.
            /// </summary>
            /// <param name="flags">Command flags.</param>
            /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
            /// <remarks>
            /// <b>No result, where <c>IRedis.Ping</c> answers a <see cref="TimeSpan"/>.</b> Most callers
            /// ping to find out whether the server answers at all, and paying for a measurement to throw
            /// it away is the wrong default; <see cref="PingMeasureAsync"/> is there for the ones that
            /// want the number. A synchronously-completed send allocates nothing here, which a
            /// <c>ValueTask&lt;TimeSpan&gt;</c> could also promise - the split is about what the caller
            /// asked for, not about the task.
            /// </remarks>
            public ValueTask PingAsync(CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
                => target.Context.SendAsync($"{RedisCommand.PING}", flags, cancellationToken);

            /// <summary>
            /// <c>PING</c>, timed: how long the round trip took.
            /// </summary>
            /// <param name="flags">Command flags.</param>
            /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
            /// <remarks>
            /// <para>
            /// <b>The handler is the clock</b>, and is therefore allocated per call - the one place on this
            /// surface where a handler is not a singleton. It takes its start timestamp in its constructor
            /// and reads the elapsed time when the reply arrives, so the duration needs no state threaded
            /// through the send. The alternative - an <c>IRespHandler&lt;TState, TResult&gt;</c> with the
            /// state handed to <c>Parse</c> - would put a type parameter on every handler in the library to
            /// serve one command.
            /// </para>
            /// <para>
            /// <b>This measures from just before the send, where <c>IRedis.Ping</c> measures from the
            /// write.</b> The shipped <c>TimingProcessor</c> reads <c>TimerMessage.StartedWritingTimestamp</c>,
            /// stamped inside <c>WriteImpl</c>, so its number excludes whatever the message spent queued -
            /// which is exactly the time a backlog adds. A handler cannot see that instant: it is handed a
            /// reader and nothing else. So this number is the same on an idle connection and larger on a
            /// congested one, and it is the caller's own latency rather than the server's.
            /// </para>
            /// <para>
            /// The reply itself is not inspected, which is the shipped behaviour too: a <c>PING</c> can be
            /// spelled several ways, an error element has already thrown by the time a handler runs, and
            /// what is being asked is how long, not what.
            /// </para>
            /// </remarks>
            public ValueTask<TimeSpan> PingMeasureAsync(CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
                => target.Context.SendAsync($"{RedisCommand.PING}", flags, new PingMeasureHandler(), cancellationToken);
        }

        /// <summary>Times a round trip by being created before it and read after it.</summary>
        /// <remarks><inheritdoc cref="PingMeasureAsync" path="/remarks/para[1]"/></remarks>
        private sealed class PingMeasureHandler : IRespHandler<TimeSpan>
        {
            private static readonly double TimestampToTicks = TimeSpan.TicksPerSecond / (double)Stopwatch.Frequency;

            private readonly long _started = Stopwatch.GetTimestamp();

            public TimeSpan Parse(ref RespReader reader)
                => new TimeSpan((long)(TimestampToTicks * (Stopwatch.GetTimestamp() - _started)));
        }

        /// <summary>The <c>NX</c>/<c>XX</c>/<c>GT</c>/<c>LT</c> token for an expiry condition.</summary>
        /// <param name="when">The condition to render.</param>
        /// <remarks>
        /// <b>Shared, because two groups need it.</b> <c>EXPIRE</c> and <c>HEXPIRE</c> take the same four
        /// tokens, so <see cref="Keys"/> and <see cref="Hashes"/> both render them - and a per-group copy
        /// is exactly the duplication the group split is otherwise removing. It stays here rather than in
        /// either group because neither owns it.
        /// </remarks>
        internal static RespFragment AsFragment(ExpireWhen when) => when switch
        {
            ExpireWhen.Always => default, // a zero-argument fragment: written, contributes nothing
            ExpireWhen.HasExpiry => RespLiterals.Xx,
            ExpireWhen.HasNoExpiry => RespLiterals.Nx,
            ExpireWhen.GreaterThanCurrentExpiry => RespLiterals.Gt,
            ExpireWhen.LessThanCurrentExpiry => RespLiterals.Lt,
            _ => throw new ArgumentOutOfRangeException(nameof(when)),
        };
    }
}
