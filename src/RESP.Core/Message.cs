using System;
using System.Threading.Tasks;

namespace Resp;

public static class Message
{
    public static TResponse Send<TRequest, TResponse>(
        in RespContext context,
        scoped ReadOnlySpan<byte> command,
        in TRequest request,
        IRespFormatter<TRequest> formatter,
        IRespParser<Void, TResponse> parser)
#if NET9_0_OR_GREATER
        where TRequest : allows ref struct
#endif
    {
        var bytes = Serialize(context.RespCommandMap, command, request, formatter, out int length);
        var msg = SyncInternalRespMessage<Void, TResponse>.Create(
            bytes,
            length,
            parser,
            in Void.Instance,
            context.CancellationToken);
        context.Connection.Send(msg);
        return msg.WaitAndRecycle(context.Connection.Configuration.SyncTimeout);
    }

    public static TResponse Send<TRequest, TState, TResponse>(
        in RespContext context,
        scoped ReadOnlySpan<byte> command,
        in TRequest request,
        in TState state,
        IRespFormatter<TRequest> formatter,
        IRespParser<TState, TResponse> parser)
#if NET9_0_OR_GREATER
        where TRequest : allows ref struct
#endif
    {
        var bytes = Serialize(context.RespCommandMap, command, in request, formatter, out int length);
        var msg = SyncInternalRespMessage<TState, TResponse>.Create(
            bytes,
            length,
            parser,
            in state,
            context.CancellationToken);
        context.Connection.Send(msg);
        return msg.WaitAndRecycle(context.Connection.Configuration.SyncTimeout);
    }

    public static ValueTask<TResponse> SendAsync<TRequest, TResponse>(
        in RespContext context,
        scoped ReadOnlySpan<byte> command,
        in TRequest request,
        IRespFormatter<TRequest> formatter,
        IRespParser<Void, TResponse> parser)
#if NET9_0_OR_GREATER
        where TRequest : allows ref struct
#endif
    {
        var bytes = Serialize(context.RespCommandMap, command, request, formatter, out int length);
        var msg = AsyncInternalRespMessage<Void, TResponse>.Create(
            bytes,
            length,
            parser,
            in Void.Instance,
            context.CancellationToken);
        context.Connection.Send(msg);
        return msg.WaitTypedAsync();
    }

    public static ValueTask<TResponse> SendAsync<TRequest, TState, TResponse>(
        in RespContext context,
        scoped ReadOnlySpan<byte> command,
        in TRequest request,
        in TState state,
        IRespFormatter<TRequest> formatter,
        IRespParser<TState, TResponse> parser)
#if NET9_0_OR_GREATER
        where TRequest : allows ref struct
#endif
    {
        var bytes = Serialize(context.RespCommandMap, command, in request, formatter, out int length);
        var msg = AsyncInternalRespMessage<TState, TResponse>.Create(
            bytes,
            length,
            parser,
            in state,
            context.CancellationToken);
        context.Connection.Send(msg);
        return msg.WaitTypedAsync();
    }

    public static void Send<TRequest>(
        in RespContext context,
        scoped ReadOnlySpan<byte> command,
        in TRequest request,
        IRespFormatter<TRequest> formatter,
        IRespParser<Void, Void> parser)
#if NET9_0_OR_GREATER
        where TRequest : allows ref struct
#endif
    {
        var bytes = Serialize(context.RespCommandMap, command, request, formatter, out int length);
        var msg = SyncInternalRespMessage<Void, Void>.Create(
            bytes,
            length,
            parser,
            in Void.Instance,
            context.CancellationToken);
        context.Connection.Send(msg);
        msg.WaitAndRecycle(context.Connection.Configuration.SyncTimeout);
    }

    public static void Send<TRequest, TState>(
        in RespContext context,
        scoped ReadOnlySpan<byte> command,
        in TRequest request,
        in TState state,
        IRespFormatter<TRequest> formatter,
        IRespParser<TState, Void> parser)
#if NET9_0_OR_GREATER
        where TRequest : allows ref struct
#endif
    {
        var bytes = Serialize(context.RespCommandMap, command, in request, formatter, out int length);
        var msg = SyncInternalRespMessage<TState, Void>.Create(
            bytes,
            length,
            parser,
            in state,
            context.CancellationToken);
        context.Connection.Send(msg);
        msg.WaitAndRecycle(context.Connection.Configuration.SyncTimeout);
    }

    public static ValueTask SendAsync<TRequest>(
        in RespContext context,
        scoped ReadOnlySpan<byte> command,
        in TRequest request,
        IRespFormatter<TRequest> formatter,
        IRespParser<Void, Void> parser)
#if NET9_0_OR_GREATER
        where TRequest : allows ref struct
#endif
    {
        var bytes = Serialize(context.RespCommandMap, command, request, formatter, out int length);
        var msg = AsyncInternalRespMessage<Void, Void>.Create(
            bytes,
            length,
            parser,
            in Void.Instance,
            context.CancellationToken);
        context.Connection.Send(msg);
        return msg.WaitUntypedAsync();
    }

    public static ValueTask SendAsync<TRequest, TState>(
        in RespContext context,
        scoped ReadOnlySpan<byte> command,
        in TRequest request,
        in TState state,
        IRespFormatter<TRequest> formatter,
        IRespParser<TState, Void> parser)
#if NET9_0_OR_GREATER
        where TRequest : allows ref struct
#endif
    {
        var bytes = Serialize(context.RespCommandMap, command, in request, formatter, out int length);
        var msg = AsyncInternalRespMessage<TState, Void>.Create(
            bytes,
            length,
            parser,
            in state,
            context.CancellationToken);
        context.Connection.Send(msg);
        return msg.WaitUntypedAsync();
    }

    private static byte[] Serialize<TRequest>(
        RespCommandMap commandMap,
        ReadOnlySpan<byte> command,
        in TRequest request,
        IRespFormatter<TRequest> formatter,
        out int length)
#if NET9_0_OR_GREATER
        where TRequest : allows ref struct
#endif
    {
        int size = 0;
        if (formatter is IRespSizeEstimator<TRequest> estimator)
        {
            size = estimator.EstimateSize(command, request);
        }

        var buffer = AmbientBufferWriter.Get(size);
        try
        {
            var writer = new RespWriter(buffer);
            if (!ReferenceEquals(commandMap, RespCommandMap.Default))
            {
                writer.CommandMap = commandMap;
            }

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
}
