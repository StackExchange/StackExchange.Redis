#if CORE_CLR
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace StackExchange.Redis
{
    public class BufferedOutputStream : Stream
    {
        private int space;
        private Stream inner;
        byte[] buffer;

        public BufferedOutputStream(Stream inner, int bufferSize)
        {
            if (inner == null) throw new ArgumentNullException(nameof(inner));
            if (bufferSize <= 0) throw new ArgumentOutOfRangeException(nameof(bufferSize));
            if (!inner.CanWrite) throw new InvalidOperationException("Inner stream is not writeable");
            this.inner = inner;
            buffer = new byte[bufferSize];
            space = bufferSize;
        }
        public override bool CanRead { get { return false; } }
        public override bool CanSeek { get { return false; } }
        public override bool CanWrite { get { return true; } }
        public override bool CanTimeout { get { return inner.CanTimeout; } }
        public override int WriteTimeout
        {
            get { return base.WriteTimeout; }
            set { base.WriteTimeout = value; }
        }
        public override void Flush()
        {
            int count = buffer.Length - space;
            if(count != 0)
            {
                inner.Write(buffer, 0, count);
                space = buffer.Length;
            }
        }
        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            int count = buffer.Length - space;
            Task result;
            if (count == 0)
            {
                result = Task.CompletedTask;
            }
            else
            {
                result = inner.WriteAsync(buffer, 0, count, cancellationToken);
                space = buffer.Length;
            }
            return result;
        }
        public override void Write(byte[] buffer, int offset, int count)
        {
            int localCount = this.buffer.Length - space;
            if (count <= space)
            {
                // fits into the existing buffer
                Buffer.BlockCopy(buffer, offset, this.buffer, localCount, count);
                space -= count;
                if (space == 0) Flush();
            } else
            {
                // do we have a partial (unsent) local buffer?
                if(localCount != 0)
                {
                    // pack it with whatever we have
                    Buffer.BlockCopy(buffer, offset, this.buffer, localCount, space);
                    offset += space;
                    count -= space;
                    Flush();
                }
                // if there are any full chunks, send those from the incoming buffer directy
                // **in multiples of the chosen size**
                while(count >= buffer.Length)
                {
                    inner.Write(buffer, offset, buffer.Length);
                    offset += buffer.Length;
                    count -= buffer.Length;
                }
                if (count != 0)
                {
                    // write anything left to the pending buffer
                    Buffer.BlockCopy(buffer, offset, this.buffer, 0, count);
                    space = buffer.Length - count;
                }
            }
        }
        public override void WriteByte(byte value)
        {
            buffer[buffer.Length - space] = value;
            if (--space == 0) Flush();
        }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            int localCount = buffer.Length - space;
            if (count <= space)
            {
                // fits into the existing buffer
                Buffer.BlockCopy(buffer, offset, this.buffer, localCount, count);
                space -= count;
                if (space == 0) return FlushAsync(cancellationToken);
                else return Task.CompletedTask;
            }
            else
            {
                return WriteAsyncMultipleWrites(buffer, offset, count, localCount, cancellationToken);
            }
        }

        private async Task WriteAsyncMultipleWrites(byte[] buffer, int offset, int count, int localCount, CancellationToken cancellationToken)
        {
            // do we have a partial (unsent) local buffer?
            if (localCount != 0)
            {
                // pack it with whatever we have
                Buffer.BlockCopy(buffer, offset, this.buffer, localCount, space);
                offset += space;
                count -= space;
                await FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            // if there are any full chunks, send those from the incoming buffer directy
            // **in multiples of the chosen size**
            while (count >= buffer.Length)
            {
                await inner.WriteAsync(buffer, offset, buffer.Length, cancellationToken).ConfigureAwait(false);
                offset += buffer.Length;
                count -= buffer.Length;
            }
            if (count != 0)
            {
                // write anything left to the pending buffer
                Buffer.BlockCopy(buffer, offset, this.buffer, 0, count);
                space = buffer.Length - count;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if(disposing)
            {
                using (inner) { inner = null; }
            }
            base.Dispose(disposing);
        }


        public override long Length { get { throw new NotSupportedException(); } }
        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }
        public override long Position {
            get { throw new NotSupportedException(); }
            set { throw new NotSupportedException(); }
        }
        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }
        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
        public override int ReadByte()
        {
            throw new NotSupportedException();
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
        public override int ReadTimeout
        {
            get { throw new NotSupportedException(); }
            set { throw new NotSupportedException(); }
        }
        public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

    }
}
#endif