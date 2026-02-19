using System;
using System.Buffers;
using System.IO;
using System.IO.Pipelines;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RESPite.Tests;

/// <summary>
/// A controllable duplex stream for testing Redis protocol interactions.
/// Captures outbound data (client-to-redis) and allows controlled inbound data (redis-to-client).
/// </summary>
public sealed class TestDuplexStream : Stream
{
    private static readonly PipeOptions s_pipeOptions = new(useSynchronizationContext: false);

    private readonly MemoryStream _outbound;
    private readonly Pipe _inbound;
    private readonly Stream _inboundStream;

    public TestDuplexStream()
    {
        _outbound = new MemoryStream();
        _inbound = new Pipe(s_pipeOptions);
        _inboundStream = _inbound.Reader.AsStream();
    }

    /// <summary>
    /// Gets the data that has been written to the stream (client-to-redis).
    /// </summary>
    public ReadOnlySpan<byte> GetOutboundData()
    {
        if (_outbound.TryGetBuffer(out var buffer))
        {
            return buffer.AsSpan();
        }
        return _outbound.GetBuffer().AsSpan(0, (int)_outbound.Length);
    }

    /// <summary>
    /// Clears the outbound data buffer.
    /// </summary>
    public void FlushOutboundData()
    {
        _outbound.Position = 0;
        _outbound.SetLength(0);
    }

    /// <summary>
    /// Adds data to the inbound buffer (redis-to-client) that will be available for reading.
    /// </summary>
    public async ValueTask AddInboundAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        await _inbound.Writer.WriteAsync(data, cancellationToken).ConfigureAwait(false);
        await _inbound.Writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Adds data to the inbound buffer (redis-to-client) that will be available for reading.
    /// Supports the "return pending.IsCompletedSynchronously ? default : AwaitAsync(pending)" pattern.
    /// </summary>
    public ValueTask AddInboundAsync(ReadOnlySpan<byte> data, CancellationToken cancellationToken = default)
    {
        // Use the Write extension method to write the span synchronously
        _inbound.Writer.Write(data);

        // Flush and return based on completion status
        var flushPending = _inbound.Writer.FlushAsync(cancellationToken);
        return flushPending.IsCompletedSuccessfully ? default : AwaitFlushAsync(flushPending);

        static async ValueTask AwaitFlushAsync(ValueTask<FlushResult> flushPending)
        {
            await flushPending.ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Adds UTF8-encoded string data to the inbound buffer (redis-to-client) that will be available for reading.
    /// Uses stack allocation for small strings (≤256 bytes) and ArrayPool for larger strings.
    /// Supports the "return pending.IsCompletedSynchronously ? default : AwaitAsync(pending)" pattern.
    /// </summary>
    public ValueTask AddInboundAsync(string data, CancellationToken cancellationToken = default)
    {
        const int StackAllocThreshold = 256;

        // Get the max byte count for UTF8 encoding
        var maxByteCount = Encoding.UTF8.GetMaxByteCount(data.Length);

        if (maxByteCount <= StackAllocThreshold)
        {
            // Use stack allocation for small strings
            Span<byte> buffer = stackalloc byte[maxByteCount];
            var actualByteCount = Encoding.UTF8.GetBytes(data, buffer);
            _inbound.Writer.Write(buffer.Slice(0, actualByteCount));
        }
        else
        {
            // Use ArrayPool for larger strings
            var buffer = ArrayPool<byte>.Shared.Rent(maxByteCount);
            try
            {
                var actualByteCount = Encoding.UTF8.GetBytes(data, buffer);
                _inbound.Writer.Write(buffer.AsSpan(0, actualByteCount));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer); // can't have been captured during write, because span
            }
        }

        // Flush and return based on completion status
        var flushPending = _inbound.Writer.FlushAsync(cancellationToken);
        return flushPending.IsCompletedSuccessfully ? default : AwaitFlushAsync(flushPending);

        static async ValueTask AwaitFlushAsync(ValueTask<FlushResult> flushPending)
        {
            await flushPending.ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Completes the inbound stream, signaling no more data will be written.
    /// </summary>
    public void CompleteInbound()
    {
        _inbound.Writer.Complete();
    }

    // Stream implementation - Read operations proxy to the inbound stream
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return _inboundStream.Read(buffer, offset, count);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return _inboundStream.ReadAsync(buffer, offset, count, cancellationToken);
    }

#if NETCOREAPP || NETSTANDARD2_1_OR_GREATER
    public override int Read(Span<byte> buffer)
    {
        return _inboundStream.Read(buffer);
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        return _inboundStream.ReadAsync(buffer, cancellationToken);
    }
#endif

    // Stream implementation - Write operations capture to the outbound stream
    public override void Write(byte[] buffer, int offset, int count)
    {
        _outbound.Write(buffer, offset, count);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return _outbound.WriteAsync(buffer, offset, count, cancellationToken);
    }

#if NETCOREAPP || NETSTANDARD2_1_OR_GREATER
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        _outbound.Write(buffer);
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        return _outbound.WriteAsync(buffer, cancellationToken);
    }
#endif

    public override void Flush()
    {
        _outbound.Flush();
    }

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        return _outbound.FlushAsync(cancellationToken);
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inbound.Writer.Complete();
            _inbound.Reader.Complete();
            _inboundStream.Dispose();
            _outbound.Dispose();
        }
        base.Dispose(disposing);
    }

#if NETCOREAPP || NETSTANDARD2_1_OR_GREATER
    public override async ValueTask DisposeAsync()
    {
        _inbound.Writer.Complete();
        _inbound.Reader.Complete();
        await _inboundStream.DisposeAsync().ConfigureAwait(false);
        await _outbound.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }
#endif
}
