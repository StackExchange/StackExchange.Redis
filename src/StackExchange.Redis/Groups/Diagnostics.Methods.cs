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
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    /// <remarks>
    /// <b>Node-local and read-only</b>, which is what the retry category says: the answer belongs to the
    /// server that was asked, so re-issuing it elsewhere would answer a different question rather than
    /// the same one again.
    /// </remarks>
    public static ValueTask<string?> LatencyDoctorAsync(this in RespDiagnostics diagnostics, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => diagnostics.Context.SendAsync<string?>(
            $"{RedisCommand.LATENCY}{RespLiterals.Doctor}", flags.WithRetryCategory(RespServerRetry.NodeLocalRead), cancellationToken: cancellationToken);

    /// <summary>LATENCY RESET: forget the recorded spikes, for some events or for all of them.</summary>
    /// <param name="diagnostics">The diagnostic command group.</param>
    /// <param name="eventNames">The events to forget; every event when empty.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    /// <remarks>
    /// <b>The empty case is a different command, not a degenerate one.</b> <c>LATENCY RESET</c> with no
    /// event names resets everything, so it cannot be short-circuited to zero the way an empty
    /// <c>SADD</c> can - and it is the overwhelmingly common call. Answers how many event time-series
    /// were reset.
    /// </remarks>
    public static ValueTask<long> LatencyResetAsync(
        this in RespDiagnostics diagnostics,
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
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    /// <remarks><inheritdoc cref="LatencyDoctorAsync" path="/remarks"/></remarks>
    public static ValueTask<LatencyHistoryEntry[]> LatencyHistoryAsync(
        this in RespDiagnostics diagnostics,
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
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    /// <remarks><inheritdoc cref="LatencyDoctorAsync" path="/remarks"/></remarks>
    public static ValueTask<LatencyLatestEntry[]> LatencyLatestAsync(
        this in RespDiagnostics diagnostics,
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
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    /// <remarks><inheritdoc cref="LatencyDoctorAsync" path="/remarks"/></remarks>
    public static ValueTask<string?> MemoryDoctorAsync(this in RespDiagnostics diagnostics, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => diagnostics.Context.SendAsync<string?>(
            $"{RedisCommand.MEMORY}{RespLiterals.Doctor}", flags.WithRetryCategory(RespServerRetry.NodeLocalRead), cancellationToken: cancellationToken);

    /// <summary>MEMORY MALLOC-STATS: the allocator's own report, verbatim.</summary>
    /// <param name="diagnostics">The diagnostic command group.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    public static ValueTask<string?> MemoryAllocatorStatsAsync(this in RespDiagnostics diagnostics, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => diagnostics.Context.SendAsync<string?>(
            $"{RedisCommand.MEMORY}{RespLiterals.MallocStats}", flags.WithRetryCategory(RespServerRetry.NodeLocalRead), cancellationToken: cancellationToken);

    /// <summary>MEMORY PURGE: ask the allocator to release what it can.</summary>
    /// <param name="diagnostics">The diagnostic command group.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    public static ValueTask MemoryPurgeAsync(this in RespDiagnostics diagnostics, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => diagnostics.Context.SendAsync(
            $"{RedisCommand.MEMORY}{RespLiterals.Purge}", flags.WithRetryCategory(RespServerRetry.NodeLocalAdmin), cancellationToken: cancellationToken);

    /// <summary>MEMORY STATS: the allocator's report, as the nested reply the server sends.</summary>
    /// <param name="diagnostics">The diagnostic command group.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    /// <remarks>
    /// <b>Handed back as a <see cref="RedisResult"/> rather than modelled</b>, which is what the shipped
    /// surface does and is right here: the reply is an open-ended, version-dependent key/value tree whose
    /// contents change between server releases, so a type for it would be a type that goes stale. The
    /// caller indexes what it recognises.
    /// </remarks>
    public static ValueTask<RedisResult> MemoryStatsAsync(
        this in RespDiagnostics diagnostics,
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
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    /// <remarks>
    /// <b>Converted here rather than by a handler</b>, because the reply is a plain unix-seconds integer
    /// and inventing a <c>DateTime</c> handler for one command would put the conversion further from the
    /// command that needs it. The fast path stays allocation-free: a synchronously-completed send is
    /// converted inline rather than awaited.
    /// </remarks>
    public static ValueTask<DateTime> LastSaveAsync(this in RespDiagnostics diagnostics, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
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
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    public static ValueTask<long> CommandCountAsync(this in RespDiagnostics diagnostics, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => diagnostics.Context.SendAsync<long>($"{RedisCommand.COMMAND}{RespLiterals.Count}", flags, cancellationToken: cancellationToken);

    /// <summary>ECHO: ask the server to say something back, as a round-trip check.</summary>
    /// <param name="diagnostics">The diagnostic command group.</param>
    /// <param name="message">What to send; the same thing comes back.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    public static ValueTask<RedisValue> EchoAsync(this in RespDiagnostics diagnostics, RedisValue message, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => diagnostics.Context.SendAsync<RedisValue>($"{RedisCommand.ECHO}{message}", flags, cancellationToken: cancellationToken);

    /// <summary>TIME: the server's own clock.</summary>
    /// <param name="diagnostics">The diagnostic command group.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    /// <remarks>
    /// The reply is a two-element array - seconds, then microseconds - so unlike <see cref="LastSaveAsync"/>
    /// this one needs a reader rather than an arithmetic conversion. The microseconds are kept: asking a
    /// server for its clock and rounding the answer to the second defeats most of the reasons to ask.
    /// </remarks>
    public static ValueTask<DateTime> TimeAsync(this in RespDiagnostics diagnostics, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => diagnostics.Context.SendAsync($"{RedisCommand.TIME}", flags.WithRetryCategory(RespServerRetry.NodeLocalRead), ServerTimeHandler.Instance, cancellationToken);

    /// <summary>SLOWLOG RESET: discard the recorded slow commands.</summary>
    /// <param name="diagnostics">The diagnostic command group.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    public static ValueTask ResetSlowLogAsync(this in RespDiagnostics diagnostics, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => diagnostics.Context.SendAsync($"{RedisCommand.SLOWLOG}{RespLiterals.Reset}", flags, cancellationToken: cancellationToken);

    /// <summary>SLOWLOG GET: the commands the server recorded as slow, newest first.</summary>
    /// <param name="diagnostics">The diagnostic command group.</param>
    /// <param name="count">How many entries to ask for; the server's own default when not positive.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    /// <remarks>
    /// <b>A non-positive <paramref name="count"/> omits the argument</b> rather than sending zero, which
    /// is the shipped behaviour and is not the same thing: <c>SLOWLOG GET 0</c> asks for no entries,
    /// where <c>SLOWLOG GET</c> asks for as many as the server volunteers.
    /// </remarks>
    public static ValueTask<CommandTrace[]> SlowLogAsync(
        this in RespDiagnostics diagnostics,
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
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    /// <remarks><inheritdoc cref="LatencyDoctorAsync" path="/remarks"/></remarks>
    public static ValueTask<string?> InfoRawAsync(
        this in RespDiagnostics diagnostics,
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
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    /// <remarks>
    /// <b>The grouping is the whole of the parse</b>, and it is shared with the shipped processor rather
    /// than written twice: <c>INFO</c> is a text format with its own quirks - <c>#</c> headers, blank
    /// lines, values containing colons - and two readers of it would be two chances to disagree about a
    /// deployment's own description of itself. See <see cref="ParseInfo"/>.
    /// </remarks>
    public static ValueTask<IGrouping<string, KeyValuePair<string, string>>[]> InfoAsync(
        this in RespDiagnostics diagnostics,
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

    /// <summary>Reads <c>TIME</c>: unix seconds, then microseconds within that second.</summary>
    private sealed class ServerTimeHandler : IRespHandler<DateTime>
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
