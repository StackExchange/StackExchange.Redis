using System;
using System.Threading;
using System.Threading.Tasks;

namespace Resp;

public interface IRespFormatter<TRequest>
{
    int Format(scoped ReadOnlySpan<byte> command, ref RespWriter writer, in TRequest request);
}
public interface IRespSizeEstimator<TRequest> : IRespFormatter<TRequest>
{
    int EstimateSize(scoped ReadOnlySpan<byte> command, in TRequest request);
}

public interface IRespParser<out TResponse>
{
    TResponse Parse(ref RespReader reader);
}
public interface IRespParser<TRequest, out TResponse>
{
    TResponse Parse(in TRequest request, ref RespReader reader);
}

public interface IRespMetadataParser // if implemented, the consumer must manually advance to the content
{
}

public static class RespConnectionExtensions
{
    public static TResponse Send<TRequest, TResponse>(this IRespConnection connection, scoped ReadOnlySpan<byte> command, TRequest request, IRespFormatter<TRequest> formatter, IRespParser<TResponse> parser)
    {
        var reqPayload = RespPayload.Create(command, request, formatter);
        request = default!; // formally release the request
        var respPayload = connection.Send(reqPayload);
        reqPayload.Dispose();

        return respPayload.ParseAndDispose(parser);
    }

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
    }
}

internal static class Singleton<T> where T : class, new()
{
    public static readonly T Instance = new();
}
