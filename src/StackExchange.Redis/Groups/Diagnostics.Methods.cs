using System;
using System.Threading;
using System.Threading.Tasks;

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

    /// <summary>SLOWLOG RESET: discard the recorded slow commands.</summary>
    /// <param name="diagnostics">The diagnostic command group.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    public static ValueTask ResetSlowLogAsync(this in RespDiagnostics diagnostics, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => diagnostics.Context.SendAsync($"{RedisCommand.SLOWLOG}{RespLiterals.Reset}", flags, cancellationToken: cancellationToken);
}
