using System.Threading;
using System.Threading.Tasks;
using RESPite.Transports;

namespace RESPite.Resp.Commands;

/// <summary>
/// Extension methods for sending <see cref="IRespCommand{TRequest, TResponse}"/> based commands.
/// </summary>
public static class RespCommandExtensions
{
    /// <summary>
    /// Perform an asynchronous command operation.
    /// </summary>
    public static ValueTask<TResponse> SendAsync<TRequest, TResponse>(
        this IAsyncMessageTransport transport,
        in TRequest request,
        CancellationToken token = default)
        where TRequest : IRespCommand<TRequest, TResponse>
        => transport.SendAsync(in request, request.Writer, request.Reader, token);

    /// <summary>
    /// Perform a synchronous command operation.
    /// </summary>
    public static TResponse Send<TRequest, TResponse>(
        this ISyncMessageTransport transport,
        in TRequest request)
        where TRequest : IRespCommand<TRequest, TResponse>
        => transport.Send(in request, request.Writer, request.Reader);

#if NET8_0_OR_GREATER
    /// <summary>
    /// Perform an asynchronous command operation.
    /// </summary>
    public static ValueTask<TResponse> SendAsync<TRequest, TResponse>(
        this IAsyncMessageTransport transport,
        CancellationToken token = default)
        where TRequest : ISharedRespCommand<TRequest, TResponse>
    {
        ref readonly TRequest request = ref TRequest.Command;
        return transport.SendAsync(in request, request.Writer, request.Reader, token);
    }

    /// <summary>
    /// Perform a synchronous command operation.
    /// </summary>
    public static TResponse Send<TRequest, TResponse>(
        this ISyncMessageTransport transport)
        where TRequest : ISharedRespCommand<TRequest, TResponse>
    {
        ref readonly TRequest request = ref TRequest.Command;
        return transport.Send(in request, request.Writer, request.Reader);
    }
#endif
}
