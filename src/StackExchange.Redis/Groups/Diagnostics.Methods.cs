using System;
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
