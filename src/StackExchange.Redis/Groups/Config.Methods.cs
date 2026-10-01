using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RESPite.Messages;
using StackExchange.Redis.Protocol;

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

    /// <summary>CONFIG SET: change one setting on this server.</summary>
    /// <param name="config">The configuration command group.</param>
    /// <param name="setting">The setting to change.</param>
    /// <param name="value">Its new value.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    /// <remarks>
    /// <b>No retry category of its own</b>, deliberately: bare <c>CONFIG</c> is already categorised as
    /// server-admin, which is what a setting change is, and the shipped spelling adds nothing either.
    /// </remarks>
    public static ValueTask SetAsync(
        this in RespConfig config,
        RedisValue setting,
        RedisValue value,
        CommandFlags flags = CommandFlags.None,
        CancellationToken cancellationToken = default)
        => config.Context.SendAsync(
            $"{RedisCommand.CONFIG}{RespLiterals.Set}{setting}{value}", flags, cancellationToken: cancellationToken);

    /// <summary>CONFIG GET, as the array <see cref="IServer"/> promises.</summary>
    /// <param name="config">The configuration command group.</param>
    /// <param name="pattern">Which settings to read; every setting when omitted.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    /// <remarks>
    /// <para>
    /// <b>Internal, because the array of string pairs is the OLD spelling</b> - the same reason
    /// <c>Hashes.GetAllArray</c> is internal. What the new surface should offer for <c>CONFIG GET</c> is
    /// a separate question from getting <see cref="IServer"/> off the <c>Message</c> path, and answering
    /// it by accident here would be a public shape chosen for a porting convenience.
    /// </para>
    /// <para>
    /// <b>An empty pattern becomes <c>*</c> rather than being omitted</b>, matching the shipped spelling:
    /// <c>CONFIG GET</c> with no pattern is an error, not "everything".
    /// </para>
    /// <para>
    /// <b>Categorised as a node-local connection read</b>, not as server-admin. <c>CONFIG</c> as a whole
    /// has to assume its most side-effecting verb, but <c>GET</c> is safe metadata - as the documentation
    /// already claimed - and the answer belongs to the node that was asked.
    /// </para>
    /// </remarks>
    internal static ValueTask<KeyValuePair<string, string>[]> GetArray(
        this in RespConfig config,
        RedisValue pattern = default,
        CommandFlags flags = CommandFlags.None,
        CancellationToken cancellationToken = default)
        => config.Context.SendAsync(
            $"{RedisCommand.CONFIG}{RespLiterals.Get}{(pattern.IsNullOrEmpty ? RedisLiterals.Wildcard : pattern)}",
            flags.WithRetryCategory(CommandFlags.CommandRetryConnection | Message.CommandServerSpecific),
            ConfigPairsHandler.Instance,
            cancellationToken);

    /// <summary>Reads a <c>CONFIG GET</c> reply, interleaved or mapped.</summary>
    /// <remarks>
    /// <b>The shipped pair reader</b>, driven directly; see <c>ResultProcessor.StringPairs</c>. Jagged is
    /// permitted and then detected from the bytes, which covers both wire shapes: RESP3 answers a map and
    /// RESP2 a flat array, and a setting's VALUE is always a scalar, so the detection cannot misfire on
    /// one.
    /// </remarks>
    private sealed class ConfigPairsHandler : IRespHandler<KeyValuePair<string, string>[]>
    {
        internal static readonly ConfigPairsHandler Instance = new();

        public KeyValuePair<string, string>[] Parse(ref RespReader reader)
            => ResultProcessor.StringPairs.ParseArray(
                ref reader, allowJagged: true, allowOversized: false, out _, state: null) ?? [];
    }
}
