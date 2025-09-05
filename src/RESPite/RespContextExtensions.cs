using System.Buffers;
using RESPite.Internal;
using RESPite.Messages;

namespace RESPite;

public static class RespContextExtensions
{
    public static RespOperationBuilder<TRequest> Command<TRequest>(
        this in RespContext context,
        ReadOnlySpan<byte> command,
        TRequest request,
        IRespFormatter<TRequest> formatter)
#if NET9_0_OR_GREATER
    where TRequest : allows ref struct
#endif
        => new(in context, command, request, formatter);

    /* not sure that default formatters (RespFormatters.Get<T>) make sense
    public static RespOperationBuilder<TRequest> Command<TRequest>(
        this in RespContext context,
        ReadOnlySpan<byte> command,
        in TRequest value)
#if NET9_0_OR_GREATER
    where TRequest : allows ref struct
#endif
        => new(in context, command, value, RespFormatters.Get<TRequest>());
        */

    public static RespOperationBuilder<bool> Command(this in RespContext context, ReadOnlySpan<byte> command)
        => new(in context, command, false, RespFormatters.Empty);

    public static RespOperationBuilder<string> Command(
        this in RespContext context,
        ReadOnlySpan<byte> command,
        string value,
        bool isKey)
        => new(in context, command, value, RespFormatters.String(isKey));

    public static RespOperationBuilder<byte[]> Command(
        this in RespContext context,
        ReadOnlySpan<byte> command,
        byte[] value,
        bool isKey)
        => new(in context, command, value, RespFormatters.ByteArray(isKey));

    /// <summary>
    /// Creates an operation and synchronously writes it to the connection.
    /// </summary>
    /// <typeparam name="TRequest">The type of the request data being sent.</typeparam>
    public static RespOperation Send<TRequest>(
        this in RespContext context,
        ReadOnlySpan<byte> command,
        in TRequest request,
        IRespFormatter<TRequest> formatter,
        IRespParser<bool> parser)
#if NET9_0_OR_GREATER
        where TRequest : allows ref struct
#endif
    {
        var op = CreateOperation(context, command, request, formatter, parser);
        context.Connection.Write(op);
        return op;
    }

    /// <summary>
    /// Creates an operation and synchronously writes it to the connection.
    /// </summary>
    /// <typeparam name="TRequest">The type of the request data being sent.</typeparam>
    /// <typeparam name="TResponse">The type of the response data being received.</typeparam>
    public static RespOperation<TResponse> Send<TRequest, TResponse>(
        this in RespContext context,
        ReadOnlySpan<byte> command,
        in TRequest request,
        IRespFormatter<TRequest> formatter,
        IRespParser<TResponse> parser)
#if NET9_0_OR_GREATER
        where TRequest : allows ref struct
#endif
    {
        var op = CreateOperation(context, command, request, formatter, parser);
        context.Connection.Write(op);
        return op;
    }

    /// <summary>
    /// Creates an operation and synchronously writes it to the connection.
    /// </summary>
    /// <typeparam name="TResponse">The type of the response data being received.</typeparam>
    public static RespOperation<TResponse> Send<TResponse>(
        this in RespContext context,
        ReadOnlySpan<byte> command,
        IRespParser<TResponse> parser)
    {
        var op = CreateOperation(context, command, false, RespFormatters.Empty, parser);
        context.Connection.Write(op);
        return op;
    }

    /// <summary>
    /// Creates an operation and synchronously writes it to the connection.
    /// </summary>
    /// <typeparam name="TRequest">The type of the request data being sent.</typeparam>
    /// <typeparam name="TState">The type of state data required by the parser.</typeparam>
    /// <typeparam name="TResponse">The type of the response data being received.</typeparam>
    public static RespOperation<TResponse> Send<TRequest, TState, TResponse>(
        this in RespContext context,
        ReadOnlySpan<byte> command,
        in TRequest request,
        IRespFormatter<TRequest> formatter,
        in TState state,
        IRespParser<TState, TResponse> parser)
#if NET9_0_OR_GREATER
        where TRequest : allows ref struct
#endif
    {
        var op = CreateOperation(context, command, request, formatter, in state, parser);
        context.Connection.Write(op);
        return op;
    }

    /// <summary>
    /// Creates an operation and synchronously writes it to the connection.
    /// </summary>
    /// <typeparam name="TState">The type of state data required by the parser.</typeparam>
    /// <typeparam name="TResponse">The type of the response data being received.</typeparam>
    public static RespOperation<TResponse> Send<TState, TResponse>(
        this in RespContext context,
        ReadOnlySpan<byte> command,
        in TState state,
        IRespParser<TState, TResponse> parser)
    {
        var op = CreateOperation(context, command, false, RespFormatters.Empty, in state, parser);
        context.Connection.Write(op);
        return op;
    }

    /// <summary>
    /// Creates an operation and asynchronously writes it to the connection, awaiting the completion of the underlying write.
    /// </summary>
    /// <typeparam name="TRequest">The type of the request data being sent.</typeparam>
    public static ValueTask<RespOperation> SendAsync<TRequest>(
        this in RespContext context,
        ReadOnlySpan<byte> command,
        in TRequest request,
        IRespFormatter<TRequest> formatter,
        IRespParser<bool> parser)
#if NET9_0_OR_GREATER
        where TRequest : allows ref struct
#endif
    {
        var op = CreateOperation(context, command, request, formatter, parser);
        var write = context.Connection.WriteAsync(op);
        if (!write.IsCompleted) return AwaitedVoid(op, write);
        write.GetAwaiter().GetResult();
        return new(op);

        static async ValueTask<RespOperation> AwaitedVoid(RespOperation op, Task write)
        {
            await write.ConfigureAwait(false);
            return op;
        }
    }

    /// <summary>
    /// Creates an operation and asynchronously writes it to the connection, awaiting the completion of the underlying write.
    /// </summary>
    /// <typeparam name="TRequest">The type of the request data being sent.</typeparam>
    /// <typeparam name="TResponse">The type of the response data being received.</typeparam>
    public static ValueTask<RespOperation<TResponse>> SendAsync<TRequest, TResponse>(
        this in RespContext context,
        ReadOnlySpan<byte> command,
        in TRequest request,
        IRespFormatter<TRequest> formatter,
        IRespParser<TResponse> parser)
#if NET9_0_OR_GREATER
        where TRequest : allows ref struct
#endif
    {
        var op = CreateOperation(context, command, request, formatter, parser);
        var write = context.Connection.WriteAsync(op);
        if (!write.IsCompleted) return Awaited(op, write);
        write.GetAwaiter().GetResult();
        return new(op);
    }

    /// <summary>
    /// Creates an operation and asynchronously writes it to the connection, awaiting the completion of the underlying write.
    /// </summary>
    /// <typeparam name="TRequest">The type of the request data being sent.</typeparam>
    /// <typeparam name="TState">The type of state data required by the parser.</typeparam>
    /// <typeparam name="TResponse">The type of the response data being received.</typeparam>
    public static ValueTask<RespOperation<TResponse>> SendAsync<TRequest, TState, TResponse>(
        this in RespContext context,
        ReadOnlySpan<byte> command,
        in TRequest request,
        IRespFormatter<TRequest> formatter,
        in TState state,
        IRespParser<TState, TResponse> parser)
#if NET9_0_OR_GREATER
        where TRequest : allows ref struct
#endif
    {
        var op = CreateOperation(context, command, request, formatter, in state, parser);
        var write = context.Connection.WriteAsync(op);
        if (!write.IsCompleted) return Awaited(op, write);
        write.GetAwaiter().GetResult();
        return new(op);
    }

    private static async ValueTask<RespOperation<T>> Awaited<T>(RespOperation<T> op, Task write)
    {
        await write.ConfigureAwait(false);
        return op;
    }

    public static RespOperation CreateOperation<TRequest>(
        in RespContext context, // deliberately not "this"
        ReadOnlySpan<byte> command,
        in TRequest request,
        IRespFormatter<TRequest> formatter,
        IRespParser<bool> parser)
#if NET9_0_OR_GREATER
        where TRequest : allows ref struct
#endif
    {
        var conn = context.Connection;
        var memory =
            conn.Serializer.Serialize(conn.NonDefaultCommandMap, command, request, formatter);
        var msg = RespStatelessMessage<bool>.Get(parser);
        msg.Init(memory, context.CancellationToken);
        return new(msg);
    }

    public static RespOperation<TResponse> CreateOperation<TRequest, TResponse>(
        in RespContext context, // deliberately not "this"
        ReadOnlySpan<byte> command,
        in TRequest request,
        IRespFormatter<TRequest> formatter,
        IRespParser<TResponse> parser)
#if NET9_0_OR_GREATER
        where TRequest : allows ref struct
#endif
    {
        var conn = context.Connection;
        var memory =
            conn.Serializer.Serialize(conn.NonDefaultCommandMap, command, request, formatter);
        var msg = RespStatelessMessage<TResponse>.Get(parser);
        msg.Init(memory, context.CancellationToken);
        return new(msg);
    }

    public static RespOperation<TResponse> CreateOperation<TRequest, TState, TResponse>(
        in RespContext context, // deliberately not "this"
        ReadOnlySpan<byte> command,
        in TRequest request,
        IRespFormatter<TRequest> formatter,
        in TState state,
        IRespParser<TState, TResponse> parser)
#if NET9_0_OR_GREATER
        where TRequest : allows ref struct
#endif
    {
        var conn = context.Connection;
        var memory = conn.Serializer.Serialize(conn.NonDefaultCommandMap, command, request, formatter);
        var msg = RespStatefulMessage<TState, TResponse>.Get(in state, parser);
        msg.Init(memory, context.CancellationToken);
        return new(msg);
    }
}
