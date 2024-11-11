using System.Buffers;
using System.Threading;
using System.Threading.Tasks;
using RESPite.Messages;
using RESPite.Transports;

namespace RESPite.Resp;

/// <summary>
/// Common RESP commands.
/// </summary>
public static class RespCommands
{
    /// <summary>
    /// Indicates the number of keys in a database.
    /// </summary>
    public static RespCommand DbSize => new RespCommand("DBSIZE");
}

internal interface IRespCommand
{
    void Write(ref RespWriter writer);
}
internal interface IRespReader<TRequest, TResponse>
{
}

internal static class RespExtensions
{
    public static ValueTask<TResponse> SendAsync<TRequest, TResponse>(
        this TRequest request,
        IAsyncMessageTransport transport,
        IReader<Empty, TResponse> reader,
        CancellationToken token = default)
        where TRequest : IRespCommand
    {
        return transport.SendAsync(request, SelfWriter<TRequest>.Instance, reader, token);
    }

    private readonly struct RequestWithReader<TRequest, TResponse>(
        TRequest request,
        IRespReader<TRequest, TResponse> reader)
    {
        public readonly TRequest Request = request;
        public readonly IRespReader<TRequest, TResponse> Reader = reader;
    }

    private class SelfWriter<TRequest, TResponse> : IWriter<RequestWithReader<TRequest, TResponse>>
        where TRequest : IRespCommand
    {
        public static readonly SelfWriter<TRequest, TResponse> Instance = new();
        private SelfWriter() { }

        public void Write(in RequestWithReader<TRequest, TResponse> request, IBufferWriter<byte> target)
        {
            var writer = new RespWriter(target);
            request.Request.Write(ref writer);
            writer.Flush();
        }
    }
}

/// <summary>
/// A parameterless RESP command.
/// </summary>
/// <param name="command">The RESP command associated with this operation.</param>
public readonly struct RespCommand(string command)
{
    /// <summary>
    /// The RESP command associated with this operation.
    /// </summary>
    public string Command => command;

    /// <summary>
    /// Sends this command to the provided transport.
    /// </summary>
    public ValueTask<TResponse> SendAsync<TResponse>(
        IAsyncMessageTransport transport,
        IReader<Empty, TResponse> reader,
        CancellationToken token = default)
        => transport.SendAsync(in this, SharedWriter.Instance, reader, token);

    /// <summary>
    /// Sends this command to the provided transport.
    /// </summary>
    public TResponse Send<TResponse>(
        ISyncMessageTransport transport,
        IReader<Empty, TResponse> reader)
        => transport.Send(in this, SharedWriter.Instance, reader);

    private sealed class SharedWriter : IWriter<RespCommand>
    {
        public static readonly SharedWriter Instance = new();
        private SharedWriter() { }

        public void Write(in RespCommand request, IBufferWriter<byte> target)
        {
            var writer = new RespWriter(target);
            writer.WriteArray(1);
            writer.WriteBulkString(request.Command);
            writer.Flush();
        }
    }
}
