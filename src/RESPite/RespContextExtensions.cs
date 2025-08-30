using System.Buffers;
using RESPite.Internal;
using RESPite.Messages;

namespace RESPite;

public static class RespContextExtensions
{
    public static RespOperationBuilder<TRequest> Command<TRequest>(
        this in RespContext context,
        ReadOnlySpan<byte> command,
        TRequest value,
        IRespFormatter<TRequest> formatter)
        => new(in context, command, value, formatter);

    /*
    public static RespOperationBuilder<T> Command<T>(
        this in RespContext context,
        ReadOnlySpan<byte> command,
        T value)
        => new(in context, command, value, RespFormatters.Get<T>());
*/

    public static RespOperationBuilder<bool> Command(this in RespContext context, ReadOnlySpan<byte> command)
        => new(in context, command, false, RespFormatters.Empty);

    /*
    public static RespOperationBuilder<string> Command(this in RespContext context, ReadOnlySpan<byte> command,
        string value, bool isKey)
        => new(in context, command, value, RespFormatters.String(isKey));

    public static RespOperationBuilder<byte[]> Command(
        this in RespContext context,
        ReadOnlySpan<byte> command,
        byte[] value,
        bool isKey)
        => new(in context, command, value, RespFormatters.ByteArray(isKey));
        */
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
        var oversized = Serialize<TRequest>(
            context.CommandMap, command, in request, formatter, out int length);
        var msg = RespMessage<TResponse>.Get(parser)
            .Init(oversized, 0, length, ArrayPool<byte>.Shared, context.CancellationToken);
        RespOperation<TResponse> operation = new(msg);
        context.Connection.Send(operation);
        return operation;
    }

    public static RespOperation<TResponse> Send<TRequest, TState, TResponse>(
        this in RespContext context,
        ReadOnlySpan<byte> command,
        in TRequest request,
        in TState state,
        IRespFormatter<TRequest> formatter,
        IRespParser<TState, TResponse> parser)
#if NET9_0_OR_GREATER
        where TRequest : allows ref struct
#endif
    {
        var oversized = Serialize<TRequest>(
            context.CommandMap, command, in request, formatter, out int length);
        var msg = RespMessage<TState, TResponse>.Get(in state, parser)
            .Init(oversized, 0, length, ArrayPool<byte>.Shared, context.CancellationToken);
        RespOperation<TResponse> operation = new(msg);
        context.Connection.Send(operation);
        return operation;
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
        throw new NotImplementedException();
        /*
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
        */
    }
}
