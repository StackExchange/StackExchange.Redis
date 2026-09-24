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

    /// <summary>SLOWLOG RESET: discard the recorded slow commands.</summary>
    /// <param name="diagnostics">The diagnostic command group.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    public static ValueTask ResetSlowLogAsync(this in RespDiagnostics diagnostics, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => diagnostics.Context.SendAsync($"{RedisCommand.SLOWLOG}{RespLiterals.Reset}", flags, cancellationToken: cancellationToken);
}
