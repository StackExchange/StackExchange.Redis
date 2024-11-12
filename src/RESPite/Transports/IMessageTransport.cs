using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;
using RESPite.Buffers;
using RESPite.Messages;

namespace RESPite.Transports;

/// <summary>
/// Message transport that supports synchronous and asynchronous calling.
/// </summary>
public interface IMessageTransport : ISyncMessageTransport, IAsyncMessageTransport // diamond
{ }

/// <summary>
/// Message transport that supports asynchronous calling.
/// </summary>
public interface IAsyncMessageTransport : IMessageTransportBase, IAsyncDisposable
{
    /// <summary>
    /// Send a message and await the response.
    /// </summary>
    ValueTask<TResponse> SendAsync<TRequest, TResponse>(in TRequest request, IWriter<TRequest> writer, IReader<TRequest, TResponse> reader, CancellationToken token = default);

    /// <summary>
    /// Send a message and await the response.
    /// </summary>
    ValueTask<TResponse> SendAsync<TRequest, TResponse>(in TRequest request, IWriter<TRequest> writer, IReader<Empty, TResponse> reader, CancellationToken token = default);
}

/// <summary>
/// Message transport that supports synchronous calling.
/// </summary>
public interface ISyncMessageTransport : IMessageTransportBase, IDisposable
{
    /// <summary>
    /// Send a message and wait for the response.
    /// </summary>
    TResponse Send<TRequest, TResponse>(in TRequest request, IWriter<TRequest> writer, IReader<TRequest, TResponse> reader);

    /// <summary>
    /// Send a message and wait for the response.
    /// </summary>
    TResponse Send<TRequest, TResponse>(in TRequest request, IWriter<TRequest> writer, IReader<Empty, TResponse> reader);
}

/// <summary>
/// Base interface for message transports.
/// </summary>
public interface IMessageTransportBase
{
    /// <summary>
    /// Indicates that out-of-band data (data not associated with a specific request) was encountered; the payload
    /// is only valid for the duration of the event. If the data is required after the invocation (for despatch),
    /// it should be retained (<see cref="BufferExtensions.Retain"/>) and released when complete.
    /// </summary>
    event MessageCallback OutOfBandData;
}
