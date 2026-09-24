using System.Threading;
using System.Threading.Tasks;

namespace StackExchange.Redis;

/// <summary>
/// The <c>CONFIG</c> commands.
/// </summary>
/// <remarks><inheritdoc cref="HyperLogLog" path="/remarks"/></remarks>
public static partial class Config
{
    /// <summary>CONFIG REWRITE: write the running configuration back to the config file.</summary>
    /// <param name="config">The configuration command group.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    public static ValueTask RewriteAsync(this in RespConfig config, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => config.Context.SendAsync($"{RedisCommand.CONFIG}{RespLiterals.Rewrite}", flags, cancellationToken: cancellationToken);

    /// <summary>CONFIG RESETSTAT: zero the statistics <c>INFO</c> reports.</summary>
    /// <param name="config">The configuration command group.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    public static ValueTask ResetStatisticsAsync(this in RespConfig config, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => config.Context.SendAsync($"{RedisCommand.CONFIG}{RespLiterals.ResetStat}", flags, cancellationToken: cancellationToken);
}
