using System;
using System.Threading;
using System.Threading.Tasks;
using Resp.RedisCommands;

namespace Resp;

public interface IRespFormatter<TRequest>
#if NET9_0_OR_GREATER
        where TRequest : allows ref struct
#endif
{
    void Format(scoped ReadOnlySpan<byte> command, ref RespWriter writer, in TRequest request);
}
public interface IRespSizeEstimator<TRequest> : IRespFormatter<TRequest>
#if NET9_0_OR_GREATER
        where TRequest : allows ref struct
#endif
{
    int EstimateSize(scoped ReadOnlySpan<byte> command, in TRequest request);
}

public interface IRespParser<out TResponse>
{
    TResponse Parse(ref RespReader reader);
}

internal interface IRespInternalMessage : IRespMessage
{
    bool AllowInlineParsing { get; }
}

internal interface IRespInlineParser // if implemented, parsing is permitted on the IO thread
{
}
public interface IRespMetadataParser // if implemented, the consumer must manually advance to the content
{
}

public static class RespConnectionExtensions
{
    /// <summary>
    /// Enforces stricter ordering guarantees, so that unawaited async operations cannot cause overlapping writes.
    /// </summary>
    public static IRespConnection ForPipeline(this IRespConnection connection)
        => connection is PipelinedConnection ? connection : new PipelinedConnection(connection);

    public static TResponse Send<TRequest, TResponse>(
        this IRespConnection connection,
        scoped ReadOnlySpan<byte> command,
        TRequest request,
        IRespFormatter<TRequest>? formatter = null,
        IRespParser<TResponse>? parser = null,
        TimeSpan timeout = default)
#if NET9_0_OR_GREATER
    where TRequest : allows ref struct
#endif
    {
        var bytes = Serialize(command, request, formatter, out int length);
        var msg = SyncInternalRespMessage<TResponse>.Create(bytes, length, parser ?? DefaultParsers.Get<TResponse>());
        connection.Send(msg);
        return msg.WaitAndRecycle(timeout);
    }

    public static Task<TResponse> SendAsync<TRequest, TResponse>(
        this IRespConnection connection,
        scoped ReadOnlySpan<byte> command,
        TRequest request,
        IRespFormatter<TRequest>? formatter = null,
        IRespParser<TResponse>? parser = null,
        CancellationToken cancellationToken = default)
#if NET9_0_OR_GREATER
    where TRequest : allows ref struct
#endif
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = Serialize(command, request, formatter, out int length);
        var msg = new AsyncInternalRespMessage<TResponse>(bytes, length, parser ?? DefaultParsers.Get<TResponse>());
        connection.Send(msg);
        return msg.WaitAsync(cancellationToken);
    }

    private static byte[] Serialize<TRequest>(ReadOnlySpan<byte> command, TRequest request, IRespFormatter<TRequest>? formatter, out int length)
#if NET9_0_OR_GREATER
        where TRequest : allows ref struct
#endif
    {
        formatter ??= DefaultFormatters.Get<TRequest>();
        int size = 0;
        if (formatter is IRespSizeEstimator<TRequest> estimator)
        {
            size = estimator.EstimateSize(command, request);
        }
        var buffer = AmbientBufferWriter.Get(size);
        try
        {
            var writer = new RespWriter(buffer);
            formatter.Format(command, ref writer, request);
            writer.Flush();
            return buffer.Detach(out length);
        }
        catch
        {
            buffer.Reset();
            throw;
        }
    }

    // public static ValueTask<RespPayload> SendAsync<TRequest>(this IRespConnection connection, scoped ReadOnlySpan<byte> command, TRequest request, IRespFormatter<TRequest>? formatter = null)
    // {
    //     var reqPayload = RespPayload.Create(command, request, formatter ?? DefaultFormatters.Get<TRequest>(), disposeOnWrite: true);
    //     return connection.SendAsync(reqPayload);
    // }

    /*
    public static ValueTask<TResponse> SendAsync<TRequest, TResponse>(this IRespConnection connection, scoped ReadOnlySpan<byte> command, TRequest request, IRespFormatter<TRequest> formatter, IRespParser<TResponse> parser, CancellationToken cancellationToken)
    {
        var reqPayload = RespPayload.Create(command, request, formatter);
        request = default!; // formally release the request
        var respPayload = connection.SendAsync(reqPayload, cancellationToken);
        return Awaited(reqPayload, respPayload, parser);

        static async ValueTask<TResponse> Awaited(RespPayload reqPayload, ValueTask<RespPayload> pending, IRespParser<TResponse> parser)
        {
            var respPayload = await pending.ConfigureAwait(false);
            reqPayload.Dispose();

            return respPayload.ParseAndDispose(parser);
        }
    }

    public static TResponse Send<TRequest, TResponse>(this IRespConnection connection, scoped ReadOnlySpan<byte> command, TRequest request, IRespFormatter<TRequest> formatter, IRespParser<TRequest, TResponse> parser)
    {
        var reqPayload = RespPayload.Create(command, request, formatter);
        var respPayload = connection.Send(reqPayload);
        reqPayload.Dispose();

        return respPayload.ParseAndDispose(in request, parser);
    }

    public static ValueTask<TResponse> SendAsync<TRequest, TResponse>(this IRespConnection connection, scoped ReadOnlySpan<byte> command, TRequest request, IRespFormatter<TRequest> formatter, IRespParser<TRequest, TResponse> parser, CancellationToken cancellationToken)
    {
        var reqPayload = RespPayload.Create(command, request, formatter);
        var respPayload = connection.SendAsync(reqPayload, cancellationToken);
        return Awaited(reqPayload, respPayload, request, parser);

        static async ValueTask<TResponse> Awaited(RespPayload reqPayload, ValueTask<RespPayload> pending, TRequest request, IRespParser<TRequest, TResponse> parser)
        {
            var respPayload = await pending.ConfigureAwait(false);
            reqPayload.Dispose();

            return respPayload.ParseAndDispose(request, parser);
        }
    }*/
}

internal static class Singleton<T> where T : class, new()
{
    public static readonly T Instance = new();
}
