using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using RESPite;
using RESPite.Messages;
using StackExchange.Redis.Protocol;

namespace StackExchange.Redis;

/// <summary>
/// The <c>SENTINEL</c> verbs, on the RESP context.
/// </summary>
/// <remarks>
/// <para>
/// <b>Internal, and on the raw context rather than a group</b>, because these exist to serve
/// <c>IServer</c>'s existing sentinel methods and the multiplexer's own failover tracking - which until now
/// went through <c>Message</c>, and so under the engine flag dialled a shipped bridge beside the new core's
/// socket. Whether sentinel deserves a public group is a surface decision this port should not make.
/// </para>
/// <para>
/// The replies are read as the shipped processors read them, including their leniencies: a missing
/// primary is <see langword="null"/> rather than an error, and entries without a usable address are skipped.
/// </para>
/// </remarks>
internal static class SentinelCommands
{
    /// <summary>SENTINEL GET-MASTER-ADDR-BY-NAME: where the named service's primary is, or null if unknown.</summary>
    internal static ValueTask<EndPoint?> SentinelPrimaryAddress(this RespContext context, string serviceName, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => context.SendAsync(
            $"{RedisCommand.SENTINEL}{RedisLiterals.GETMASTERADDRBYNAME}{serviceName.AsRedisValue()}",
            flags.WithRetryCategory(RespServerRetry.NodeLocalRead),
            PrimaryAddressHandler.Instance,
            cancellationToken);

    /// <summary>SENTINEL SENTINELS: the addresses of the other sentinels watching the named service.</summary>
    internal static ValueTask<EndPoint[]> SentinelSentinelAddresses(this RespContext context, string serviceName, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => context.SendAsync(
            $"{RedisCommand.SENTINEL}{RedisLiterals.SENTINELS}{serviceName.AsRedisValue()}",
            flags.WithRetryCategory(RespServerRetry.NodeLocalRead),
            AddressesHandler.Instance,
            cancellationToken);

    /// <summary>SENTINEL REPLICAS (or SLAVES, before 5.0): the addresses of the named service's replicas.</summary>
    internal static ValueTask<EndPoint[]> SentinelReplicaAddresses(this RespContext context, string serviceName, bool replicaCommands, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => context.SendAsync(
            $"{RedisCommand.SENTINEL}{(replicaCommands ? RedisLiterals.REPLICAS : RedisLiterals.SLAVES)}{serviceName.AsRedisValue()}",
            flags.WithRetryCategory(RespServerRetry.NodeLocalRead),
            AddressesHandler.Instance,
            cancellationToken);

    /// <summary>SENTINEL MASTER: the named service's primary, described as field/value pairs.</summary>
    internal static ValueTask<KeyValuePair<string, string>[]> SentinelPrimary(this RespContext context, string serviceName, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => context.SendAsync(
            $"{RedisCommand.SENTINEL}{RedisLiterals.MASTER}{serviceName.AsRedisValue()}",
            flags.WithRetryCategory(RespServerRetry.NodeLocalRead),
            PairsHandler.Instance,
            cancellationToken);

    /// <summary>SENTINEL MASTERS: every monitored primary, each described as field/value pairs.</summary>
    internal static ValueTask<KeyValuePair<string, string>[][]> SentinelPrimaries(this RespContext context, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => context.SendAsync(
            $"{RedisCommand.SENTINEL}{RedisLiterals.MASTERS}",
            flags.WithRetryCategory(RespServerRetry.NodeLocalRead),
            PairsArrayHandler.Instance,
            cancellationToken);

    /// <summary>SENTINEL REPLICAS (or SLAVES): the named service's replicas, each described as field/value pairs.</summary>
    internal static ValueTask<KeyValuePair<string, string>[][]> SentinelReplicas(this RespContext context, string serviceName, bool replicaCommands, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => context.SendAsync(
            $"{RedisCommand.SENTINEL}{(replicaCommands ? RedisLiterals.REPLICAS : RedisLiterals.SLAVES)}{serviceName.AsRedisValue()}",
            flags.WithRetryCategory(RespServerRetry.NodeLocalRead),
            PairsArrayHandler.Instance,
            cancellationToken);

    /// <summary>SENTINEL SENTINELS: the other sentinels watching the named service, each as field/value pairs.</summary>
    internal static ValueTask<KeyValuePair<string, string>[][]> SentinelSentinels(this RespContext context, string serviceName, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => context.SendAsync(
            $"{RedisCommand.SENTINEL}{RedisLiterals.SENTINELS}{serviceName.AsRedisValue()}",
            flags.WithRetryCategory(RespServerRetry.NodeLocalRead),
            PairsArrayHandler.Instance,
            cancellationToken);

    /// <summary>SENTINEL FAILOVER: force a failover of the named service, as if the primary were unreachable.</summary>
    /// <remarks>Not node-local-read: it changes the deployment, so it keeps the caller's flags as they are.</remarks>
    internal static ValueTask SentinelFailover(this RespContext context, string serviceName, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => context.SendAsync(
            $"{RedisCommand.SENTINEL}{RedisLiterals.FAILOVER}{serviceName.AsRedisValue()}",
            flags,
            cancellationToken: cancellationToken);

    /// <summary>A <c>host port</c> pair, or nothing - the shipped reading of GET-MASTER-ADDR-BY-NAME.</summary>
    private sealed class PrimaryAddressHandler : IRespHandler<EndPoint?>
    {
        internal static readonly PrimaryAddressHandler Instance = new();

        public EndPoint? Parse(ref RespReader reader)
        {
            if (reader.IsNull || (reader.IsAggregate && reader.AggregateLengthIs(0))) return null;
            if (reader.IsAggregate && reader.AggregateLengthIs(2)
                && reader.TryMoveNext() && reader.ReadString() is { } host
                && reader.TryMoveNext() && reader.TryReadInt64(out var port))
            {
                return Format.ParseEndPoint(host, checked((int)port));
            }

            throw new RespException("Unexpected SENTINEL GET-MASTER-ADDR-BY-NAME reply.");
        }
    }

    /// <summary>
    /// The address of each entry in an array of field/value descriptions, read from its <c>ip</c> and
    /// <c>port</c> fields; entries without both are skipped, as the shipped processors skip them.
    /// </summary>
    private sealed class AddressesHandler : IRespHandler<EndPoint[]>
    {
        internal static readonly AddressesHandler Instance = new();

        public EndPoint[] Parse(ref RespReader reader)
        {
            if (reader.IsNull) return [];
            if (!reader.IsAggregate) throw new RespException("Unexpected SENTINEL address-list reply.");

            var endpoints = reader.ReadPastArray(
                static (ref RespReader item) =>
                {
                    if (!item.IsAggregate) return null;

                    string? host = null;
                    long port = 0;
                    while (item.TryMoveNext() && item.IsScalar)
                    {
                        var isIp = item.Is("ip"u8);
                        var isPort = !isIp && item.Is("port"u8);
                        if (!(item.TryMoveNext() && item.IsScalar)) break;
                        if (isIp) host = item.ReadString();
                        else if (isPort) item.TryReadInt64(out port);
                    }

                    return host is not null && port > 0 ? Format.ParseEndPoint(host, checked((int)port)) : null;
                },
                scalar: false);

            if (endpoints is null) return [];
            var found = new List<EndPoint>(endpoints.Length);
            foreach (var endpoint in endpoints)
            {
                if (endpoint is not null) found.Add(endpoint);
            }
            return found.ToArray();
        }
    }

    /// <summary>One description as field/value pairs, interleaved or mapped - the shipped pair reader.</summary>
    private sealed class PairsHandler : IRespHandler<KeyValuePair<string, string>[]>
    {
        internal static readonly PairsHandler Instance = new();

        public KeyValuePair<string, string>[] Parse(ref RespReader reader)
            => ResultProcessor.StringPairs.ParseArray(
                ref reader, allowJagged: true, allowOversized: false, out _, state: null) ?? [];
    }

    /// <summary>An array of descriptions, each read by <see cref="PairsHandler"/>.</summary>
    private sealed class PairsArrayHandler : IRespHandler<KeyValuePair<string, string>[][]>
    {
        internal static readonly PairsArrayHandler Instance = new();

        public KeyValuePair<string, string>[][] Parse(ref RespReader reader)
        {
            if (reader.IsNull) return [];
            if (!reader.IsAggregate) throw new RespException("Unexpected SENTINEL description-list reply.");

            return reader.ReadPastArray(
                static (ref RespReader item) => item.IsAggregate
                    ? PairsHandler.Instance.Parse(ref item)
                    : throw new RespException("Unexpected SENTINEL description: expected an array."),
                scalar: false) ?? [];
        }
    }
}
