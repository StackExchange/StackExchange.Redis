using System.Buffers;
using RESPite.Messages;

namespace RESPite.Resp.Commands;

/// <summary>
/// A simple RESP command where the request and response are independent.
/// </summary>
public interface IRespCommand<TRequest, TResponse>
{
    /// <summary>
    /// Formatter for the request type, <typeparamref name="TRequest"/>.
    /// </summary>
    IWriter<TRequest> Writer { get; }

    /// <summary>
    /// Parser for the response type, <typeparamref name="TResponse"/>.
    /// </summary>
    IReader<Empty, TResponse> Reader { get; }
}

/// <summary>
/// Formatter for RESP requests of type <typeparamref name="TRequest"/>.
/// </summary>
public abstract class CommandWriter<TRequest> : IWriter<TRequest>
{
    void IWriter<TRequest>.Write(in TRequest request, IBufferWriter<byte> target)
    {
        var writer = new RespWriter(target);
        Write(in request, ref writer);
        writer.Flush();
    }

    /// <summary>
    /// Format the request.
    /// </summary>
    protected abstract void Write(in TRequest request, ref RespWriter writer);
}

/// <summary>
/// Parser for RESP responses of type <typeparamref name="TResponse"/>.
/// </summary>
public abstract class CommandReader<TResponse> : IReader<Empty, TResponse>
{
    TResponse IReader<Empty, TResponse>.Read(in Empty request, in ReadOnlySequence<byte> content)
    {
        var reader = new RespReader(content);
        return Read(ref reader);
    }

    /// <summary>
    /// Parse the response.
    /// </summary>
    protected abstract TResponse Read(ref RespReader reader);
}
