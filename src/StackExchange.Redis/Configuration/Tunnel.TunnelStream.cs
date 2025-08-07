using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace StackExchange.Redis.Configuration;

public abstract partial class Tunnel
{
    /// <summary>
    /// Stream decorator intended for use with <see cref="Tunnel"/> implementations.
    /// </summary>
    /// <param name="tail">The wrapped stream.</param>
    protected abstract class TunnelStream(Stream tail) : Stream
    {
        private readonly Stream _tail = tail;

        /// <inheritdoc />
        public override bool CanRead => _tail.CanRead;

        /// <inheritdoc />
        public override bool CanWrite => _tail.CanWrite;

        /// <inheritdoc />
        public override bool CanSeek => false; // duplex

        /// <inheritdoc />
        public override bool CanTimeout => _tail.CanTimeout;

        /// <inheritdoc />
        public override int ReadTimeout
        {
            get => _tail.ReadTimeout;
            set => _tail.ReadTimeout = value;
        }

        /// <inheritdoc />
        public override int WriteTimeout
        {
            get => _tail.WriteTimeout;
            set => _tail.WriteTimeout = value;
        }

        /// <inheritdoc />
        public sealed override long Length => throw new NotSupportedException(); // duplex

        /// <inheritdoc />
        public sealed override long Position
        {
            get => throw new NotSupportedException(); // duplex
            set => throw new NotSupportedException(); // duplex
        }

        /// <inheritdoc />
        public sealed override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException(); // duplex

        /// <inheritdoc />
        public sealed override void SetLength(long value) => throw new NotSupportedException(); // duplex

        // we don't use these APIs

        /// <inheritdoc />
        public sealed override IAsyncResult BeginRead(
            byte[] buffer, int offset, int count, AsyncCallback? callback, object? state)
            => throw new NotSupportedException();

        /// <inheritdoc />
        public sealed override IAsyncResult BeginWrite(
            byte[] buffer, int offset, int count, AsyncCallback? callback, object? state)
            => throw new NotSupportedException();

        /// <inheritdoc />
        public sealed override int EndRead(IAsyncResult asyncResult) => throw new NotSupportedException();

        /// <inheritdoc />
        public sealed override void EndWrite(IAsyncResult asyncResult) => throw new NotSupportedException();

        /// <inheritdoc />
        public override void Flush() => _tail.Flush();

        /// <inheritdoc />
        public override Task FlushAsync(CancellationToken cancellationToken)
            => _tail.FlushAsync(cancellationToken);

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _tail.Dispose();
            }

            base.Dispose(disposing);
        }

        /// <inheritdoc />
        public override void Close()
        {
            _tail.Close();
            base.Close();
        }

        /// <inheritdoc />
        public override int ReadByte() => _tail.ReadByte();

        /// <inheritdoc />
        public override int Read(byte[] buffer, int offset, int count) => _tail.Read(buffer, offset, count);

        /// <inheritdoc />
        public override Task<int> ReadAsync(
            byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => _tail.ReadAsync(buffer, offset, count, cancellationToken);

        /// <inheritdoc />
        public override void WriteByte(byte value) => _tail.WriteByte(value);

        /// <inheritdoc />
        public override void Write(byte[] buffer, int offset, int count) => _tail.Write(buffer, offset, count);

        /// <inheritdoc />
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => _tail.WriteAsync(buffer, offset, count, cancellationToken);

        /// <inheritdoc />
        public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
            => _tail.CopyToAsync(destination, bufferSize, cancellationToken);

#if NETCOREAPP3_0_OR_GREATER
        /// <inheritdoc />
        public override void CopyTo(Stream destination, int bufferSize) => _tail.CopyTo(destination, bufferSize);

        /// <inheritdoc />
        public override ValueTask DisposeAsync() => _tail.DisposeAsync();

        /// <inheritdoc />
        public override int Read(Span<byte> buffer) => _tail.Read(buffer);

        /// <inheritdoc />
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
            => _tail.ReadAsync(buffer, cancellationToken);

        /// <inheritdoc />
        public override void Write(ReadOnlySpan<byte> buffer) => _tail.Write(buffer);

        /// <inheritdoc />
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
            => _tail.WriteAsync(buffer, cancellationToken);
#endif
    }
}
