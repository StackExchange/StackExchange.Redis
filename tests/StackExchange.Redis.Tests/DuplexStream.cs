using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Combines separate input and output streams into a single duplex stream.
/// </summary>
internal sealed class DuplexStream(Stream inputStream, Stream outputStream) : Stream
{
    private readonly Stream _inputStream = inputStream ?? throw new ArgumentNullException(nameof(inputStream));
    private readonly Stream _outputStream = outputStream ?? throw new ArgumentNullException(nameof(outputStream));

    public override bool CanRead => _inputStream.CanRead;
    public override bool CanWrite => _outputStream.CanWrite;
    public override bool CanSeek => false;
    public override bool CanTimeout => _inputStream.CanTimeout || _outputStream.CanTimeout;

    public override int ReadTimeout
    {
        get => _inputStream.ReadTimeout;
        set => _inputStream.ReadTimeout = value;
    }

    public override int WriteTimeout
    {
        get => _outputStream.WriteTimeout;
        set => _outputStream.WriteTimeout = value;
    }

    public override long Length => throw new NotSupportedException($"{nameof(DuplexStream)} does not support seeking.");
    public override long Position
    {
        get => throw new NotSupportedException($"{nameof(DuplexStream)} does not support seeking.");
        set => throw new NotSupportedException($"{nameof(DuplexStream)} does not support seeking.");
    }

    public override int Read(byte[] buffer, int offset, int count)
        => _inputStream.Read(buffer, offset, count);

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => _inputStream.ReadAsync(buffer, offset, count, cancellationToken);

#if NET
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        => _inputStream.ReadAsync(buffer, cancellationToken);

    public override int Read(Span<byte> buffer)
        => _inputStream.Read(buffer);
#endif

    public override int ReadByte()
        => _inputStream.ReadByte();

    public override void Write(byte[] buffer, int offset, int count)
        => _outputStream.Write(buffer, offset, count);

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => _outputStream.WriteAsync(buffer, offset, count, cancellationToken);

#if NET
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        => _outputStream.WriteAsync(buffer, cancellationToken);

    public override void Write(ReadOnlySpan<byte> buffer)
        => _outputStream.Write(buffer);
#endif

    public override void WriteByte(byte value)
        => _outputStream.WriteByte(value);

    public override void Flush()
        => _outputStream.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken)
        => _outputStream.FlushAsync(cancellationToken);

    public override long Seek(long offset, SeekOrigin origin)
        => throw new NotSupportedException($"{nameof(DuplexStream)} does not support seeking.");

    public override void SetLength(long value)
        => throw new NotSupportedException($"{nameof(DuplexStream)} does not support seeking.");

    public override void Close()
    {
        _inputStream.Close();
        _outputStream.Close();
        base.Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inputStream.Dispose();
            _outputStream.Dispose();
        }
        base.Dispose(disposing);
    }

#if NET
    public override async ValueTask DisposeAsync()
    {
        await _inputStream.DisposeAsync().ConfigureAwait(false);
        await _outputStream.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }
#endif

    public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback? callback, object? state)
        => _inputStream.BeginRead(buffer, offset, count, callback, state);

    public override int EndRead(IAsyncResult asyncResult)
        => _inputStream.EndRead(asyncResult);

    public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback? callback, object? state)
        => _outputStream.BeginWrite(buffer, offset, count, callback, state);

    public override void EndWrite(IAsyncResult asyncResult)
        => _outputStream.EndWrite(asyncResult);

#if NET
    public override void CopyTo(Stream destination, int bufferSize)
        => _inputStream.CopyTo(destination, bufferSize);
#endif

    public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        => _inputStream.CopyToAsync(destination, bufferSize, cancellationToken);
}
