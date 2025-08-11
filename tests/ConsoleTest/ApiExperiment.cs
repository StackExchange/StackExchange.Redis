using System.Buffers;

namespace StackExchange.Redis;

internal static class Code
{
    public static void Run()
    {
        RespContext ctx = default;
        var obj = ctx with { KeyPrefix = "abc", AsyncState = null, };
    }
}

public readonly struct RespContext
{
    // public CancellationToken CancellationToken { get; init; }
    public object? AsyncState { get; init; }
    public RedisKey KeyPrefix { get; init; }
}

public interface IRespExecutor
{
    IMuxer Multiplexer { get; }
    RespContext Context { get; }

    RespPayload Execute(RespPayload payload);
}

static class RespExecutorExtensions
{
    public static TResponse Execute<TRequest, TResponse>(this IRespExecutor executor, TRequest request, RespFormatter<TRequest> formatter,
        RespParser<TResponse> parser)
    {
        RespWriter writer = executor.Multiplexer.CreateWriter();
        var key = formatter(ref writer, request);

        RespPayload outbound = null!;
        using var inbound = executor.Execute(payload);
        outbound.Dispose(); // intentionally not "using", for defined behaviour
        var reader = new RespReader(inbound.Body);
        return parser(ref reader);
    }
}

public sealed class RespPayload : IDisposable
{
    public int Database { get; }
    public CommandFlags Flags { get; }
    public RedisKey RoutingKey { get; }
    public ReadOnlySequence<byte> Body { get; }
    public void Dispose() { }

    internal int ValidateAndCount() => throw new NotImplementedException();
}

public ref struct RespWriter
{
}

public ref struct RespReader
{
}

static class Strings
{
    public static RedisValue Get(this IRespDatabase database, RedisKey key)
        => database.Execute(key, )
}

public delegate RedisKey RespFormatter<in TRequest>(ref RespWriter writer, TRequest request);
public delegate TResponse RespParser<out TResponse>(ref RespReader reader);

public class ConnectionRespExecutor : IRespExecutor
{
    // single dedicated connection, no routing
}

public class RoutedRespExecutor : IRespExecutor
{
    // pick connection based on keys, flags, etc
}

public interface IRespDatabase : IRespExecutor
{
    int Database { get; }
}

public interface IRespServer : IRespExecutor
{
}

public interface IMuxer
{
    IRespDatabase GetRespDatabase();
    IRespServer GetRespServer();
    RespWriter CreateWriter();
}
public class Muxer : IMuxer
{
    public IRespDatabase GetRespDatabase() => throw new NotImplementedException();
    public IRespServer GetRespServer() => throw new NotImplementedException();
}
