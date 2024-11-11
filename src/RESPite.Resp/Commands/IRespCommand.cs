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
