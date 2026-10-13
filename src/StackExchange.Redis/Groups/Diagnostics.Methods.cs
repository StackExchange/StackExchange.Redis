using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RESPite;
using RESPite.Messages;
using StackExchange.Redis.Protocol;

namespace StackExchange.Redis;

/// <summary>
/// The commands that ask a server about itself.
/// </summary>
/// <remarks><inheritdoc cref="HyperLogLog" path="/remarks"/></remarks>
public static partial class Diagnostics
{
    /// <summary>LATENCY DOCTOR: the server's own prose report on its latency.</summary>
    /// <param name="diagnostics">The diagnostic command group.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request: one not yet written is never sent; one already written still runs on the server, and its reply is discarded.</param>
    /// <remarks>
    /// <b>Node-local and read-only</b>, which is what the retry category says: the answer belongs to the
    /// server that was asked, so re-issuing it elsewhere would answer a different question rather than
    /// the same one again.
    /// </remarks>
    public static ValueTask<string?> LatencyDoctorAsync(this RespDiagnostics diagnostics, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => diagnostics.Context.SendAsync<string?>(
            $"{RedisCommand.LATENCY}{RespLiterals.Doctor}", flags.WithRetryCategory(RespServerRetry.NodeLocalRead), cancellationToken: cancellationToken);

    /// <summary>LATENCY RESET: forget the recorded spikes, for some events or for all of them.</summary>
    /// <param name="diagnostics">The diagnostic command group.</param>
    /// <param name="eventNames">The events to forget; every event when empty.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request: one not yet written is never sent; one already written still runs on the server, and its reply is discarded.</param>
    /// <remarks>
    /// <b>The empty case is a different command, not a degenerate one.</b> <c>LATENCY RESET</c> with no
    /// event names resets everything, so it cannot be short-circuited to zero the way an empty
    /// <c>SADD</c> can - and it is the overwhelmingly common call. Answers how many event time-series
    /// were reset.
    /// </remarks>
    public static ValueTask<long> LatencyResetAsync(
        this RespDiagnostics diagnostics,
        ReadOnlySpan<RedisValue> eventNames = default,
        CommandFlags flags = CommandFlags.None,
        CancellationToken cancellationToken = default)
        => eventNames.IsEmpty
            ? diagnostics.Context.SendAsync<long>(
                $"{RedisCommand.LATENCY}{RespLiterals.Reset}", flags, cancellationToken: cancellationToken)
            : diagnostics.Context.SendAsync<long>(
                $"{RedisCommand.LATENCY}{RespLiterals.Reset}{eventNames}", flags, cancellationToken: cancellationToken);

    /// <summary>LATENCY HISTORY: every recorded spike for one event, oldest first.</summary>
    /// <param name="diagnostics">The diagnostic command group.</param>
    /// <param name="eventName">The event to report on.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request: one not yet written is never sent; one already written still runs on the server, and its reply is discarded.</param>
    /// <remarks><inheritdoc cref="LatencyDoctorAsync" path="/remarks"/></remarks>
    public static ValueTask<LatencyHistoryEntry[]> LatencyHistoryAsync(
        this RespDiagnostics diagnostics,
        RedisValue eventName,
        CommandFlags flags = CommandFlags.None,
        CancellationToken cancellationToken = default)
        => diagnostics.Context.SendAsync(
            $"{RedisCommand.LATENCY}{RespLiterals.History}{eventName}",
            flags.WithRetryCategory(RespServerRetry.NodeLocalRead),
            LatencyHandler.History,
            cancellationToken);

    /// <summary>LATENCY LATEST: the most recent spike for each event, with that event's worst.</summary>
    /// <param name="diagnostics">The diagnostic command group.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request: one not yet written is never sent; one already written still runs on the server, and its reply is discarded.</param>
    /// <remarks><inheritdoc cref="LatencyDoctorAsync" path="/remarks"/></remarks>
    public static ValueTask<LatencyLatestEntry[]> LatencyLatestAsync(
        this RespDiagnostics diagnostics,
        CommandFlags flags = CommandFlags.None,
        CancellationToken cancellationToken = default)
        => diagnostics.Context.SendAsync(
            $"{RedisCommand.LATENCY}{RespLiterals.Latest}",
            flags.WithRetryCategory(RespServerRetry.NodeLocalRead),
            LatencyHandler.Latest,
            cancellationToken);

    /// <summary>MEMORY DOCTOR: the server's own prose report on its memory use.</summary>
    /// <param name="diagnostics">The diagnostic command group.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request: one not yet written is never sent; one already written still runs on the server, and its reply is discarded.</param>
    /// <remarks><inheritdoc cref="LatencyDoctorAsync" path="/remarks"/></remarks>
    public static ValueTask<string?> MemoryDoctorAsync(this RespDiagnostics diagnostics, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => diagnostics.Context.SendAsync<string?>(
            $"{RedisCommand.MEMORY}{RespLiterals.Doctor}", flags.WithRetryCategory(RespServerRetry.NodeLocalRead), cancellationToken: cancellationToken);

    /// <summary>MEMORY MALLOC-STATS: the allocator's own report, verbatim.</summary>
    /// <param name="diagnostics">The diagnostic command group.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request: one not yet written is never sent; one already written still runs on the server, and its reply is discarded.</param>
    public static ValueTask<string?> MemoryAllocatorStatsAsync(this RespDiagnostics diagnostics, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => diagnostics.Context.SendAsync<string?>(
            $"{RedisCommand.MEMORY}{RespLiterals.MallocStats}", flags.WithRetryCategory(RespServerRetry.NodeLocalRead), cancellationToken: cancellationToken);

    /// <summary>MEMORY PURGE: ask the allocator to release what it can.</summary>
    /// <param name="diagnostics">The diagnostic command group.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request: one not yet written is never sent; one already written still runs on the server, and its reply is discarded.</param>
    public static ValueTask MemoryPurgeAsync(this RespDiagnostics diagnostics, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => diagnostics.Context.SendAsync(
            $"{RedisCommand.MEMORY}{RespLiterals.Purge}", flags.WithRetryCategory(RespServerRetry.NodeLocalAdmin), cancellationToken: cancellationToken);

    /// <summary>MEMORY STATS: the allocator's report, as the nested reply the server sends.</summary>
    /// <param name="diagnostics">The diagnostic command group.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request: one not yet written is never sent; one already written still runs on the server, and its reply is discarded.</param>
    /// <remarks>
    /// <b>Handed back as a <see cref="RedisResult"/> rather than modelled</b>, which is what the shipped
    /// surface does and is right here: the reply is an open-ended, version-dependent key/value tree whose
    /// contents change between server releases, so a type for it would be a type that goes stale. The
    /// caller indexes what it recognises.
    /// </remarks>
    public static ValueTask<RedisResult> MemoryStatsAsync(
        this RespDiagnostics diagnostics,
        CommandFlags flags = CommandFlags.None,
        CancellationToken cancellationToken = default)
        => diagnostics.Context.SendAsync(
            $"{RedisCommand.MEMORY}{RespLiterals.Stats}",
            flags.WithRetryCategory(RespServerRetry.NodeLocalRead),
            RedisResultHandler.Instance,
            cancellationToken);

    /// <summary>LASTSAVE: when the last successful save of the dataset finished.</summary>
    /// <param name="diagnostics">The diagnostic command group.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request: one not yet written is never sent; one already written still runs on the server, and its reply is discarded.</param>
    /// <remarks>
    /// <b>Converted here rather than by a handler</b>, because the reply is a plain unix-seconds integer
    /// and inventing a <c>DateTime</c> handler for one command would put the conversion further from the
    /// command that needs it. The fast path stays allocation-free: a synchronously-completed send is
    /// converted inline rather than awaited.
    /// </remarks>
    public static ValueTask<DateTime> LastSaveAsync(this RespDiagnostics diagnostics, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
    {
        var pending = diagnostics.Context.SendAsync<long>(
            $"{RedisCommand.LASTSAVE}", flags.WithRetryCategory(RespServerRetry.NodeLocalRead), cancellationToken: cancellationToken);

        return pending.IsCompletedSuccessfully
            ? new(RedisBase.UnixEpoch.AddSeconds(pending.GetAwaiter().GetResult()))
            : Awaited(pending);

        static async ValueTask<DateTime> Awaited(ValueTask<long> pending)
            => RedisBase.UnixEpoch.AddSeconds(await pending.ConfigureAwait(false));
    }

    /// <summary>COMMAND COUNT: how many commands this server knows.</summary>
    /// <param name="diagnostics">The diagnostic command group.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request: one not yet written is never sent; one already written still runs on the server, and its reply is discarded.</param>
    public static ValueTask<long> CommandCountAsync(this RespDiagnostics diagnostics, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => diagnostics.Context.SendAsync<long>($"{RedisCommand.COMMAND}{RespLiterals.Count}", flags, cancellationToken: cancellationToken);

    /// <summary>SHUTDOWN: ask the server to stop, with or without saving first.</summary>
    /// <param name="diagnostics">The diagnostic command group.</param>
    /// <param name="mode">Whether to save, not save, or leave it to the server's configuration.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request: one not yet written is never sent; one already written still runs on the server, and its reply is discarded.</param>
    /// <remarks>
    /// <b>A command whose success looks like a failure</b>: a server that obeys stops, so the socket
    /// closes and there is no <c>+OK</c> to read. The caller of this still sees that as a connection
    /// fault - swallowing it here would hide a <c>SHUTDOWN</c> that was refused, which is the one outcome
    /// worth knowing about - so the decision stays with
    /// <see cref="IServer.Shutdown(ShutdownMode, CommandFlags)"/>, which knows it asked.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> is not a known mode.</exception>
    internal static ValueTask ShutdownAsync(
        this RespDiagnostics diagnostics,
        ShutdownMode mode = ShutdownMode.Default,
        CommandFlags flags = CommandFlags.None,
        CancellationToken cancellationToken = default)
        => mode switch
        {
            ShutdownMode.Default => diagnostics.Context.SendAsync(
                $"{RedisCommand.SHUTDOWN}", flags, cancellationToken: cancellationToken),
            ShutdownMode.Always => diagnostics.Context.SendAsync(
                $"{RedisCommand.SHUTDOWN}{RespLiterals.Save}", flags, cancellationToken: cancellationToken),
            ShutdownMode.Never => diagnostics.Context.SendAsync(
                $"{RedisCommand.SHUTDOWN}{RespLiterals.NoSave}", flags, cancellationToken: cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };

    /// <summary>BGREWRITEAOF, BGSAVE or SAVE: ask the server to persist its dataset.</summary>
    /// <param name="diagnostics">The diagnostic command group.</param>
    /// <param name="type">Which kind of save to ask for.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request: one not yet written is never sent; one already written still runs on the server, and its reply is discarded.</param>
    /// <remarks>
    /// <para>
    /// <b>Three commands rather than one with a mode</b>, because that is what the server has, and the
    /// two background ones answer a status line rather than <c>+OK</c> - which is checked rather than
    /// ignored: "background saving started" is the difference between a save that is happening and a
    /// server that said something else entirely. <c>ScalarSays</c> is the shipped check, shared.
    /// </para>
    /// <para>
    /// <b>The caller gets no value back</b>, matching <see cref="IServer.Save(SaveType, CommandFlags)"/>:
    /// the only interesting outcome is "the server did not agree", and that arrives as an exception.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="type"/> is not a known save type.</exception>
    internal static ValueTask SaveAsync(
        this RespDiagnostics diagnostics,
        SaveType type,
        CommandFlags flags = CommandFlags.None,
        CancellationToken cancellationToken = default)
        => type switch
        {
            SaveType.BackgroundRewriteAppendOnlyFile => Discard(diagnostics.Context.SendAsync(
                $"{RedisCommand.BGREWRITEAOF}", flags, SaveStartedHandler.Aof, cancellationToken)),
            SaveType.BackgroundSave => Discard(diagnostics.Context.SendAsync(
                $"{RedisCommand.BGSAVE}", flags, SaveStartedHandler.Rdb, cancellationToken)),
#pragma warning disable CS0618 // SAVE is obsolete; IServer still offers it, so this still has to send it
            SaveType.ForegroundSave => diagnostics.Context.SendAsync(
                $"{RedisCommand.SAVE}", flags, cancellationToken: cancellationToken),
#pragma warning restore CS0618
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };

    /// <summary>Await a typed send for its side effect, discarding the value.</summary>
    /// <param name="pending">The send.</param>
    private static async ValueTask Discard(ValueTask<bool> pending) => _ = await pending.ConfigureAwait(false);

    /// <summary>ROLE: what this server is, and who is on the other side of the relationship.</summary>
    /// <param name="diagnostics">The diagnostic command group.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request: one not yet written is never sent; one already written still runs on the server, and its reply is discarded.</param>
    /// <remarks>
    /// <para>
    /// <b>Null when the reply could not be read</b>, which is the shipped behaviour and the right one:
    /// <c>ROLE</c> is how a client asks what a server IS, and inventing an answer for an unreadable reply
    /// would be worse than admitting there isn't one.
    /// </para>
    /// <para>
    /// <b>Not the same question as the handshake's <c>ROLE</c></b>, which this core already asks. That one
    /// wants a routing fact - primary or replica, and which peers - and keeps it in the topology; this
    /// hands the caller the whole modelled reply, replication offsets included. One command, two
    /// consumers with different appetites.
    /// </para>
    /// <para>
    /// <inheritdoc cref="CommandGetKeysArray" path="/remarks/node()[1]"/>
    /// </para>
    /// </remarks>
    internal static ValueTask<Role?> RoleAsync(
        this RespDiagnostics diagnostics,
        CommandFlags flags = CommandFlags.None,
        CancellationToken cancellationToken = default)
        => diagnostics.Context.SendAsync(
            $"{RedisCommand.ROLE}", flags, RoleHandler.Instance, cancellationToken);

    /// <summary>CLIENT LIST: every connection this server currently has, including this one.</summary>
    /// <param name="diagnostics">The diagnostic command group.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request: one not yet written is never sent; one already written still runs on the server, and its reply is discarded.</param>
    /// <remarks>
    /// <para>
    /// <b>In the diagnostic group rather than a <c>Client</c> one</b>, because that is what the question
    /// is: "what does this server see?". The other <c>CLIENT</c> verbs are a different kind of thing -
    /// <c>SETNAME</c>/<c>SETINFO</c> and <c>TRACKING</c> are the handshake telling the server about US,
    /// and <c>KILL</c> is administration - so a group named for the command word would collect things
    /// that have nothing to do with each other.
    /// </para>
    /// <para>
    /// <inheritdoc cref="CommandGetKeysArray" path="/remarks/node()[1]"/>
    /// </para>
    /// </remarks>
    internal static ValueTask<ClientInfo[]> ClientListArray(
        this RespDiagnostics diagnostics,
        CommandFlags flags = CommandFlags.None,
        CancellationToken cancellationToken = default)
        => diagnostics.Context.SendAsync(
            $"{RedisCommand.CLIENT}{RespLiterals.List}", flags, ClientListHandler.Instance, cancellationToken);

    /// <summary>CLIENT KILL: close the connections a filter describes, and say how many.</summary>
    /// <param name="diagnostics">The diagnostic command group.</param>
    /// <param name="filter">The filter, already rendered - it begins with <c>KILL</c>.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request: one not yet written is never sent; one already written still runs on the server, and its reply is discarded.</param>
    /// <remarks>
    /// <para>
    /// <b>Here under protest, as <see cref="ClientListArray"/> says: this is administration, not
    /// diagnosis.</b> It sits in this group because an admin group does not exist yet and inventing one
    /// for a single verb would decide the shape of a public surface as a side effect of a port. The
    /// filter is rendered by <c>ClientKillFilter</c> so that both spellings of the public API - the
    /// filter object and the four loose arguments - produce one wire form.
    /// </para>
    /// <para>
    /// <b>Why it had to move at all.</b> The v3 overloads built a <c>Message</c>, so under the engine
    /// flag they went down a pipeline with nothing on the other end while <c>CLIENT LIST</c> beside them -
    /// already ported - answered from this core. <c>ClientKillTests</c> reads that as a cancelled task:
    /// the command never reached a server, and killing a client is not something to report as a count.
    /// </para>
    /// <para>
    /// <inheritdoc cref="CommandGetKeysArray" path="/remarks/node()[1]"/>
    /// </para>
    /// </remarks>
    internal static ValueTask<long> ClientKillCount(
        this RespDiagnostics diagnostics,
        ReadOnlySpan<RedisValue> filter,
        CommandFlags flags = CommandFlags.None,
        CancellationToken cancellationToken = default)
        => diagnostics.Context.SendAsync(
            $"{RedisCommand.CLIENT}{filter}",
            flags.WithRetryCategory(RespServerRetry.NodeLocalAdmin),
            RespHandlers.Int64,
            cancellationToken);

    /// <summary>CLUSTER NODES: the answering node's own view of the cluster, as the text it sends.</summary>
    /// <param name="diagnostics">The diagnostic command group.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request: one not yet written is never sent; one already written still runs on the server, and its reply is discarded.</param>
    /// <remarks>
    /// <b>Raw, because parsing it needs a server</b>: <c>ClusterConfiguration</c> is built against the node
    /// that answered and the selection strategy, neither of which a context has. <c>RedisServer</c> parses it
    /// and records it, as the handshake's caller does. Internal while the home of the cluster verbs is
    /// undecided - this group takes them only so the port does not decide a public surface by accident.
    /// A node-local read: the answer is that node's belief, so it is safe to replay against that node.
    /// </remarks>
    internal static ValueTask<string?> ClusterNodesRaw(
        this RespDiagnostics diagnostics,
        CommandFlags flags = CommandFlags.None,
        CancellationToken cancellationToken = default)
        => diagnostics.Context.SendAsync(
            $"{RedisCommand.CLUSTER}{RespLiterals.Nodes}",
            flags.WithRetryCategory(RespServerRetry.NodeLocalRead),
            RespHandlers.String,
            cancellationToken);

    /// <summary>CLUSTER SLOTS: the answering node's slot map.</summary>
    /// <param name="diagnostics">The diagnostic command group.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request: one not yet written is never sent; one already written still runs on the server, and its reply is discarded.</param>
    /// <remarks>
    /// The handshake's parser, so the public call and topology discovery cannot read the same reply two
    /// ways. Internal for the reason <see cref="ClusterNodesRaw"/> gives.
    /// </remarks>
    internal static ValueTask<ClusterSlotsResult?> ClusterSlots(
        this RespDiagnostics diagnostics,
        CommandFlags flags = CommandFlags.None,
        CancellationToken cancellationToken = default)
        => diagnostics.Context.SendAsync(
            $"{RedisCommand.CLUSTER}{RespLiterals.Slots}",
            flags.WithRetryCategory(RespServerRetry.NodeLocalRead),
            RespHandshake.ClusterSlotsHandler.Instance,
            cancellationToken);

    /// <summary>CLIENT KILL &lt;addr&gt;: close one connection by address, the original positional form.</summary>
    /// <param name="diagnostics">The diagnostic command group.</param>
    /// <param name="address">The address to close, as the server spells it.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request: one not yet written is never sent; one already written still runs on the server, and its reply is discarded.</param>
    /// <remarks>
    /// <b>Separate from <see cref="ClientKillCount"/> because the wire form is, and so is the reply.</b>
    /// The positional spelling predates the filters, answers <c>+OK</c> rather than a count, and is the
    /// only thing the oldest servers accept - so it is sent as written rather than reworded into
    /// <c>ADDR</c>, which would change what a caller's existing code reaches.
    /// </remarks>
    internal static ValueTask ClientKillAddress(
        this RespDiagnostics diagnostics,
        RedisValue address,
        CommandFlags flags = CommandFlags.None,
        CancellationToken cancellationToken = default)
        => diagnostics.Context.SendAsync(
            $"{RedisCommand.CLIENT}{RespLiterals.Kill}{address}",
            flags.WithRetryCategory(RespServerRetry.NodeLocalAdmin),
            cancellationToken: cancellationToken);

    /// <summary>COMMAND GETKEYS: which of a command's arguments the server considers keys.</summary>
    /// <param name="diagnostics">The diagnostic command group.</param>
    /// <param name="command">The command and its arguments, as they would be sent.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request: one not yet written is never sent; one already written still runs on the server, and its reply is discarded.</param>
    /// <remarks>
    /// <b>Internal, because <see cref="RedisKey"/><c>[]</c> is the old spelling</b> - as with
    /// <c>Config.GetArray</c>. The answer is also the server's opinion about a command this client may
    /// not model at all, which is a surface question worth deciding on its own rather than during a port.
    /// </remarks>
    internal static ValueTask<RedisKey[]> CommandGetKeysArray(
        this RespDiagnostics diagnostics,
        ReadOnlySpan<RedisValue> command,
        CommandFlags flags = CommandFlags.None,
        CancellationToken cancellationToken = default)
        => diagnostics.Context.SendAsync(
            $"{RedisCommand.COMMAND}{RespLiterals.GetKeys}{command}", flags, RespHandlers.KeyArray, cancellationToken);

    /// <summary>COMMAND LIST: the commands this server knows, optionally filtered.</summary>
    /// <param name="diagnostics">The diagnostic command group.</param>
    /// <param name="moduleName">List only the commands a module added.</param>
    /// <param name="category">List only the commands in an ACL category.</param>
    /// <param name="pattern">List only the commands matching a glob.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request: one not yet written is never sent; one already written still runs on the server, and its reply is discarded.</param>
    /// <remarks>
    /// <para>
    /// <b>At most one filter</b>, because <c>FILTERBY</c> takes one and the server would reject two. The
    /// shipped surface offers all three as independent optional parameters and throws when more than one
    /// is given, which this keeps: the signature it has to serve is already public.
    /// </para>
    /// <para>
    /// <inheritdoc cref="CommandGetKeysArray" path="/remarks/node()[1]"/>
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">More than one filter was given.</exception>
    internal static ValueTask<string[]> CommandListArray(
        this RespDiagnostics diagnostics,
        RedisValue? moduleName = null,
        RedisValue? category = null,
        RedisValue? pattern = null,
        CommandFlags flags = CommandFlags.None,
        CancellationToken cancellationToken = default)
    {
        var filters = (moduleName is null ? 0 : 1) + (category is null ? 0 : 1) + (pattern is null ? 0 : 1);
        if (filters > 1) throw new ArgumentException("More then one filter is not allowed");

        var handler = RespHandlers.StringArray;
        if (moduleName is { } module)
        {
            return diagnostics.Context.SendAsync(
                $"{RedisCommand.COMMAND}{RespLiterals.List}{RespLiterals.FilterBy}{RespLiterals.Module}{module}",
                flags,
                handler,
                cancellationToken);
        }

        if (category is { } acl)
        {
            return diagnostics.Context.SendAsync(
                $"{RedisCommand.COMMAND}{RespLiterals.List}{RespLiterals.FilterBy}{RespLiterals.AclCat}{acl}",
                flags,
                handler,
                cancellationToken);
        }

        if (pattern is { } glob)
        {
            return diagnostics.Context.SendAsync(
                $"{RedisCommand.COMMAND}{RespLiterals.List}{RespLiterals.FilterBy}{RespLiterals.Pattern}{glob}",
                flags,
                handler,
                cancellationToken);
        }

        return diagnostics.Context.SendAsync(
            $"{RedisCommand.COMMAND}{RespLiterals.List}", flags, handler, cancellationToken);
    }

    /// <summary>ECHO: ask the server to say something back, as a round-trip check.</summary>
    /// <param name="diagnostics">The diagnostic command group.</param>
    /// <param name="message">What to send; the same thing comes back.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request: one not yet written is never sent; one already written still runs on the server, and its reply is discarded.</param>
    public static ValueTask<RedisValue> EchoAsync(this RespDiagnostics diagnostics, RedisValue message, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => diagnostics.Context.SendAsync<RedisValue>($"{RedisCommand.ECHO}{message}", flags, cancellationToken: cancellationToken);

    /// <summary>TIME: the server's own clock.</summary>
    /// <param name="diagnostics">The diagnostic command group.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request: one not yet written is never sent; one already written still runs on the server, and its reply is discarded.</param>
    /// <remarks>
    /// The reply is a two-element array - seconds, then microseconds - so unlike <see cref="LastSaveAsync"/>
    /// this one needs a reader rather than an arithmetic conversion. The microseconds are kept: asking a
    /// server for its clock and rounding the answer to the second defeats most of the reasons to ask.
    /// </remarks>
    public static ValueTask<DateTime> TimeAsync(this RespDiagnostics diagnostics, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => diagnostics.Context.SendAsync($"{RedisCommand.TIME}", flags.WithRetryCategory(RespServerRetry.NodeLocalRead), ServerTimeHandler.Instance, cancellationToken);

    /// <summary>SLOWLOG RESET: discard the recorded slow commands.</summary>
    /// <param name="diagnostics">The diagnostic command group.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request: one not yet written is never sent; one already written still runs on the server, and its reply is discarded.</param>
    public static ValueTask ResetSlowLogAsync(this RespDiagnostics diagnostics, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => diagnostics.Context.SendAsync($"{RedisCommand.SLOWLOG}{RespLiterals.Reset}", flags, cancellationToken: cancellationToken);

    /// <summary>SLOWLOG GET: the commands the server recorded as slow, newest first.</summary>
    /// <param name="diagnostics">The diagnostic command group.</param>
    /// <param name="count">How many entries to ask for; the server's own default when not positive.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request: one not yet written is never sent; one already written still runs on the server, and its reply is discarded.</param>
    /// <remarks>
    /// <b>A non-positive <paramref name="count"/> omits the argument</b> rather than sending zero, which
    /// is the shipped behaviour and is not the same thing: <c>SLOWLOG GET 0</c> asks for no entries,
    /// where <c>SLOWLOG GET</c> asks for as many as the server volunteers.
    /// </remarks>
    public static ValueTask<CommandTrace[]> SlowLogAsync(
        this RespDiagnostics diagnostics,
        int count = 0,
        CommandFlags flags = CommandFlags.None,
        CancellationToken cancellationToken = default)
        => count > 0
            ? diagnostics.Context.SendAsync(
                $"{RedisCommand.SLOWLOG}{RespLiterals.Get}{count}",
                flags.WithRetryCategory(RespServerRetry.NodeLocalRead),
                SlowLogHandler.Instance,
                cancellationToken)
            : diagnostics.Context.SendAsync(
                $"{RedisCommand.SLOWLOG}{RespLiterals.Get}",
                flags.WithRetryCategory(RespServerRetry.NodeLocalRead),
                SlowLogHandler.Instance,
                cancellationToken);

    /// <summary>INFO: everything the server will say about itself, verbatim.</summary>
    /// <param name="diagnostics">The diagnostic command group.</param>
    /// <param name="section">One section, or every section when omitted.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request: one not yet written is never sent; one already written still runs on the server, and its reply is discarded.</param>
    /// <remarks><inheritdoc cref="LatencyDoctorAsync" path="/remarks"/></remarks>
    public static ValueTask<string?> InfoRawAsync(
        this RespDiagnostics diagnostics,
        RedisValue section = default,
        CommandFlags flags = CommandFlags.None,
        CancellationToken cancellationToken = default)
        => section.IsNullOrEmpty
            ? diagnostics.Context.SendAsync<string?>(
                $"{RedisCommand.INFO}", flags.WithRetryCategory(RespServerRetry.NodeLocalRead), cancellationToken: cancellationToken)
            : diagnostics.Context.SendAsync<string?>(
                $"{RedisCommand.INFO}{section}", flags.WithRetryCategory(RespServerRetry.NodeLocalRead), cancellationToken: cancellationToken);

    /// <summary>INFO, grouped by the section headers the server writes into it.</summary>
    /// <param name="diagnostics">The diagnostic command group.</param>
    /// <param name="section">One section, or every section when omitted.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request: one not yet written is never sent; one already written still runs on the server, and its reply is discarded.</param>
    /// <remarks>
    /// <b>The grouping is the whole of the parse</b>, and it is shared with the shipped processor rather
    /// than written twice: <c>INFO</c> is a text format with its own quirks - <c>#</c> headers, blank
    /// lines, values containing colons - and two readers of it would be two chances to disagree about a
    /// deployment's own description of itself. See <see cref="ParseInfo"/>.
    /// </remarks>
    public static ValueTask<IGrouping<string, KeyValuePair<string, string>>[]> InfoAsync(
        this RespDiagnostics diagnostics,
        RedisValue section = default,
        CommandFlags flags = CommandFlags.None,
        CancellationToken cancellationToken = default)
        => section.IsNullOrEmpty
            ? diagnostics.Context.SendAsync(
                $"{RedisCommand.INFO}", flags.WithRetryCategory(RespServerRetry.NodeLocalRead), InfoHandler.Instance, cancellationToken)
            : diagnostics.Context.SendAsync(
                $"{RedisCommand.INFO}{section}", flags.WithRetryCategory(RespServerRetry.NodeLocalRead), InfoHandler.Instance, cancellationToken);

    /// <summary>Group an <c>INFO</c> payload by its <c>#</c> section headers.</summary>
    /// <param name="text">The reply, or null when the server said nothing.</param>
    /// <remarks>
    /// <b>One implementation, two callers.</b> <c>ResultProcessor.Info</c> reads the same format for the
    /// shipped pipeline; keeping the parse here and calling it from there means a deployment cannot be
    /// described two different ways depending on which core asked. Lines before any header - and any line
    /// with no header above it at all - are "miscellaneous", which is the shipped behaviour and what
    /// callers index by.
    /// </remarks>
    internal static IGrouping<string, KeyValuePair<string, string>>[] ParseInfo(string? text)
    {
        var category = NormalizeSection(null);
        var list = new List<Tuple<string, KeyValuePair<string, string>>>();
        if (text is not null)
        {
            using var lines = new System.IO.StringReader(text);
            while (lines.ReadLine() is string line)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.StartsWith("# ", StringComparison.Ordinal))
                {
                    category = NormalizeSection(line.Substring(2));
                    continue;
                }

                var idx = line.IndexOf(':');
                if (idx < 0) continue;
                list.Add(Tuple.Create(
                    category,
                    new KeyValuePair<string, string>(line.Substring(0, idx).Trim(), line.Substring(idx + 1).Trim())));
            }
        }

        return list.GroupBy(x => x.Item1, x => x.Item2).ToArray();
    }

    private static string NormalizeSection(string? category)
        => string.IsNullOrWhiteSpace(category) ? "miscellaneous" : category!.Trim();

    /// <summary>Reads <c>INFO</c> and groups it; see <see cref="ParseInfo"/>.</summary>
    private sealed class InfoHandler : IRespHandler<IGrouping<string, KeyValuePair<string, string>>[]>
    {
        internal static readonly InfoHandler Instance = new();

        public IGrouping<string, KeyValuePair<string, string>>[] Parse(ref RespReader reader)
        {
            if (!reader.IsScalar) throw new RespException("Unexpected INFO reply.");
            return ParseInfo(reader.IsNull ? null : reader.ReadString());
        }
    }

    /// <summary>Reads the two <c>LATENCY</c> array shapes, each as the array the older surface promises.</summary>
    /// <remarks>
    /// One instance, two explicit implementations, as the stream group's handler does: the two parses
    /// differ only in return type, which C# cannot overload on. The element walks are the <b>shipped</b>
    /// ones - <see cref="LatencyHistoryEntry.TryParseEntry"/> and
    /// <see cref="LatencyLatestEntry.TryParseEntry"/> - so there is one reading of a server's latency
    /// report rather than one per core.
    /// </remarks>
    private sealed class LatencyHandler : IRespHandler<LatencyHistoryEntry[]>, IRespHandler<LatencyLatestEntry[]>
    {
        private static readonly LatencyHandler Instance = new();

        /// <summary>A <c>LATENCY HISTORY</c> reply.</summary>
        internal static IRespHandler<LatencyHistoryEntry[]> History => Instance;

        /// <summary>A <c>LATENCY LATEST</c> reply.</summary>
        internal static IRespHandler<LatencyLatestEntry[]> Latest => Instance;

        LatencyHistoryEntry[] IRespHandler<LatencyHistoryEntry[]>.Parse(ref RespReader reader)
        {
            if (!reader.IsAggregate) throw new RespException("Unexpected LATENCY HISTORY reply.");
            return reader.ReadPastArray(
                static (ref RespReader r) => LatencyHistoryEntry.TryParseEntry(ref r, out var parsed)
                    ? parsed : throw new RespException("Unexpected LATENCY HISTORY element."),
                scalar: false) ?? [];
        }

        LatencyLatestEntry[] IRespHandler<LatencyLatestEntry[]>.Parse(ref RespReader reader)
        {
            if (!reader.IsAggregate) throw new RespException("Unexpected LATENCY LATEST reply.");
            return reader.ReadPastArray(
                static (ref RespReader r) => LatencyLatestEntry.TryParseEntry(ref r, out var parsed)
                    ? parsed : throw new RespException("Unexpected LATENCY LATEST element."),
                scalar: false) ?? [];
        }
    }

    /// <summary>Reads <c>SLOWLOG GET</c>; see <see cref="CommandTrace.ParseArray"/>.</summary>
    private sealed class SlowLogHandler : IRespHandler<CommandTrace[]>
    {
        internal static readonly SlowLogHandler Instance = new();

        public CommandTrace[] Parse(ref RespReader reader)
            => CommandTrace.ParseArray(ref reader) ?? throw new RespException("Unexpected SLOWLOG GET reply.");
    }

    /// <summary>Checks that a background save said it had started.</summary>
    /// <remarks>
    /// <b>A prefix match, not an equality one</b>, because the server appends its own detail after
    /// "Background saving started" and has changed what it appends. The check itself is
    /// <c>RespParsers.ScalarSays</c>, once shared with the v3 processors so the two cores could not
    /// disagree about whether a server agreed.
    /// </remarks>
    internal sealed class SaveStartedHandler : IRespHandler<bool>
    {
        /// <summary>A <c>BGSAVE</c> reply.</summary>
        internal static readonly SaveStartedHandler Rdb = new(aof: false);

        /// <summary>A <c>BGREWRITEAOF</c> reply.</summary>
        internal static readonly SaveStartedHandler Aof = new(aof: true);

        private readonly bool _aof;

        private SaveStartedHandler(bool aof) => _aof = aof;

        public bool Parse(ref RespReader reader)
        {
            var expected = _aof
                ? RespParsers.Literals.background_aof_rewriting_started.Hash
                : RespParsers.Literals.background_saving_started.Hash;

            return RespParsers.ScalarSays(ref reader, in expected, startsWith: true)
                ? true
                : throw new RespException("The server did not report that a background save had started.");
        }
    }

    /// <summary>Reads <c>ROLE</c>; see <c>ResultProcessor.ParseRole</c>.</summary>
    private sealed class RoleHandler : IRespHandler<Role?>
    {
        internal static readonly RoleHandler Instance = new();

        public Role? Parse(ref RespReader reader) => RespParsers.ParseRole(ref reader);
    }

    /// <summary>Reads <c>CLIENT LIST</c>: one text block, one line per client.</summary>
    /// <remarks>
    /// <b>The shipped line parser</b>, <c>ClientInfo.TryParse</c>, reused rather than rewritten - the
    /// format is a space-separated key=value list whose fields vary by server version, and two readers of
    /// it would be two chances to disagree. Same argument as <c>ParseInfo</c>.
    /// </remarks>
    private sealed class ClientListHandler : IRespHandler<ClientInfo[]>
    {
        internal static readonly ClientListHandler Instance = new();

        public ClientInfo[] Parse(ref RespReader reader)
            => reader.Prefix is RespPrefix.BulkString or RespPrefix.VerbatimString
                && ClientInfo.TryParse(reader.ReadString(), out var clients)
                    ? clients
                    : throw new RespException("Unexpected CLIENT LIST reply.");
    }

    /// <summary>Reads <c>TIME</c>: unix seconds, then microseconds within that second.</summary>
    internal sealed class ServerTimeHandler : IRespHandler<DateTime>
    {
        internal static readonly ServerTimeHandler Instance = new();

        public DateTime Parse(ref RespReader reader)
        {
            if (reader.IsAggregate
                && reader.TryMoveNext() && reader.IsScalar && reader.TryReadInt64(out var seconds)
                && reader.TryMoveNext() && reader.IsScalar && reader.TryReadInt64(out var micros)
                && !reader.TryMoveNext())
            {
                // DateTime ticks are 100ns, so a microsecond is ten of them
                return RedisBase.UnixEpoch.AddSeconds(seconds).AddTicks(micros * 10);
            }

            throw new RespException("Unexpected TIME reply.");
        }
    }
}
