using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;

namespace RESPite.Transports;

/// <summary>
/// Final endpoint for byte-based transports.
/// </summary>
public interface IByteTransportBase
{
    /// <summary>
    /// Get the current buffered data.
    /// </summary>
    ReadOnlySequence<byte> GetBuffer();

    /// <summary>
    /// Complete a read operation, optionally discarding some quantity of data.
    /// </summary>
    public void Advance(long consumed);
}

/// <summary>
/// A <see cref="IByteTransportBase"/> that supports both synchronous and asynchronous access.
/// </summary>
public interface IByteTransport : ISyncByteTransport, IAsyncByteTransport// diamond pattern
{
}

/// <summary>
/// A <see cref="IByteTransportBase"/> that supports asynchronous access.
/// </summary>
public interface IAsyncByteTransport : IByteTransportBase, IAsyncDisposable
{
    /// <summary>
    /// Asynchronously read more data into a buffer.
    /// </summary>
    /// <param name="hint">The amount of additional data (in bytes) desired.</param>
    /// <param name="token">Cancellation.</param>
    ValueTask<bool> TryReadAsync(int hint, CancellationToken token = default);

    /// <summary>
    /// Asynchronously write data.
    /// </summary>
    ValueTask WriteAsync(in ReadOnlySequence<byte> buffer, CancellationToken token = default);
}

/// <summary>
/// A <see cref="IByteTransportBase"/> that supports synchronous access.
/// </summary>
public interface ISyncByteTransport : IByteTransportBase, IDisposable
{
    /// <summary>
    /// Synchronously read more data into a buffer.
    /// </summary>
    /// <param name="hint">The amount of additional data (in bytes) desired.</param>
    bool TryRead(int hint);

    /// <summary>
    /// Synchronously write data.
    /// </summary>
    void Write(in ReadOnlySequence<byte> buffer);
}
