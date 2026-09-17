using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using RESPite;

namespace StackExchange.Redis.Protocol
{
    /// <summary>
    /// EXPERIMENTAL SPIKE. An <see cref="IBufferWriter{T}"/> that turns what <c>MessageWriter</c> already
    /// writes into a <see cref="RespRequestFrame"/> - so the existing <c>Message</c> surface can feed the new
    /// pipeline without rewriting any of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The transition plan this exists for: <c>RedisDatabase</c> builds a <c>Message</c> and hands it a
    /// <c>ResultProcessor&lt;T&gt;</c>. Those are the same two halves as the new API - a request that renders
    /// itself, and something that turns a reply into a result - so the existing several-thousand-method
    /// command surface can be pointed at the new cache without being touched.
    /// </para>
    /// <para>
    /// <b>The one thing bytes cannot supply is which arguments were keys</b>, which is why this is a writer
    /// and not a post-pass over the rendered frame. A rendered frame is just N bulk strings; key-ness is
    /// writer-side semantics. <c>MessageWriter</c> happens to have kept that distinction - <c>Write(in
    /// RedisKey)</c> is a separate overload from <c>WriteBulkString(in RedisValue)</c> - so it can report
    /// keys as it writes them, and that is the entire integration.
    /// </para>
    /// </remarks>
    public sealed class RespFrameWriter : IBufferWriter<byte>
    {
        private byte[] _buffer;
        private int _offset;
        private int[] _keyOffsets = new int[4];
        private int _keyCount;

        /// <summary>Create a writer with an initial capacity.</summary>
        /// <param name="capacity">Initial buffer size hint.</param>
        public RespFrameWriter(int capacity = 256) => _buffer = ArrayPool<byte>.Shared.Rent(Math.Max(16, capacity));

        /// <summary>Begin again, for reuse across messages.</summary>
        public void Reset()
        {
            _offset = 0;
            _keyCount = 0;
        }

        /// <summary>Record that a key starts at the current write position.</summary>
        /// <remarks>
        /// Called by <c>MessageWriter</c> immediately before it writes a key. This is the only hook the
        /// integration needs, and it carries the one fact the bytes do not.
        /// </remarks>
        internal void MarkKey()
        {
            if (_keyCount == _keyOffsets.Length) Array.Resize(ref _keyOffsets, _keyCount * 2);
            _keyOffsets[_keyCount++] = _offset;
        }

        /// <inheritdoc/>
        public void Advance(int count) => _offset += count;

        /// <inheritdoc/>
        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            Ensure(sizeHint);
            return _buffer.AsMemory(_offset);
        }

        /// <inheritdoc/>
        public Span<byte> GetSpan(int sizeHint = 0)
        {
            Ensure(sizeHint);
            return _buffer.AsSpan(_offset);
        }

        /// <summary>The bytes written so far.</summary>
        public ReadOnlySpan<byte> Span => _buffer.AsSpan(0, _offset);

        /// <summary>
        /// Take the rendered bytes as a <see cref="RespRequestFrame"/>, transferring the buffer; the writer rents a
        /// fresh one for its next message.
        /// </summary>
        /// <param name="slot">
        /// The cluster slot, which <c>Message.GetHashSlot</c> already computes - so unlike the interpolated
        /// writer there is no need to fold it during the write.
        /// </param>
        /// <remarks>
        /// The frame's <c>Command</c> is left UNKNOWN: this writer is fed by <c>MessageWriter</c>, which
        /// already has a <c>Message</c> carrying the command, so nothing downstream of here would learn
        /// anything from a copy of it. The interpolated writer is the one that has to record it, because
        /// there the frame IS the whole message.
        /// </remarks>
        public RespRequestFrame Complete(int slot = ServerSelectionStrategy.NoSlot)
        {
            var buffer = _buffer;
            var length = _offset;
            var frame = new RespRequestFrame(buffer, 0, length, ReadArgCount(buffer, length), slot, PackKeyMarks(buffer, length), RedisCommand.UNKNOWN);

            _buffer = ArrayPool<byte>.Shared.Rent(Math.Max(16, length));
            Reset();
            return frame;
        }

        // the header MessageWriter already wrote says how many arguments there are
        private static int ReadArgCount(byte[] buffer, int length)
        {
            if (length < 2 || buffer[0] != (byte)'*') return 0;
            var count = 0;
            for (var i = 1; i < length && buffer[i] != (byte)'\r'; i++)
            {
                count = (count * 10) + (buffer[i] - (byte)'0');
            }

            return count;
        }

        /// <summary>Pack recorded key offsets into the frame's single 64-bit field.</summary>
        /// <remarks>
        /// Two keys or fewer keep their byte offsets and need no scan. Beyond that the frame's encoding is a
        /// bitmap of ARGUMENT indices, which offsets are not - so derive them by walking the frame once,
        /// here, off any hot path. That is the same walk <see cref="RespRequestFrame.TryGetKeys"/> does in reverse.
        /// </remarks>
        private ulong PackKeyMarks(byte[] buffer, int length)
        {
            if (_keyCount == 0) return 0;
            if (_keyCount <= 2)
            {
                var a = (ulong)_keyOffsets[0] & RespRequestFrame.SlotMask;
                var b = _keyCount == 2 ? ((ulong)_keyOffsets[1] & RespRequestFrame.SlotMask) << RespRequestFrame.SlotBits : 0;
                return a | b;
            }

            ulong bitmap = 0;
            var i = 0;
            while (i < length && buffer[i] != (byte)'\n') i++; // past '*N\r\n'
            i++;

            var arg = 0;
            while (i < length)
            {
                if (Array.IndexOf(_keyOffsets, i, 0, _keyCount) >= 0)
                {
                    if (arg <= RespRequestFrame.MaxBitmapArg) bitmap |= 1UL << arg;
                    else bitmap |= RespRequestFrame.TruncatedFlag;
                }

                var j = i + 1;
                var len = 0;
                while (j < length && buffer[j] != (byte)'\r')
                {
                    len = (len * 10) + (buffer[j] - (byte)'0');
                    j++;
                }

                i = j + 2 + len + 2;
                arg++;
            }

            return RespRequestFrame.OverflowFlag | bitmap;
        }

        private void Ensure(int sizeHint)
        {
            if (sizeHint <= 0) sizeHint = 1;
            if (_buffer.Length - _offset >= sizeHint) return;

            var bigger = ArrayPool<byte>.Shared.Rent(Math.Max(_buffer.Length * 2, _offset + sizeHint));
            Buffer.BlockCopy(_buffer, 0, bigger, 0, _offset);
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = bigger;
        }
    }
}
