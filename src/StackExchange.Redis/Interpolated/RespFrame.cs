using System;
using System.Buffers;

namespace StackExchange.Redis.Interpolated
{
    /// <summary>
    /// EXPERIMENTAL SPIKE. A rendered RESP frame, plus the routing and invalidation metadata that was
    /// folded while it was being written.
    /// </summary>
    internal struct RespFrame : IDisposable
    {
        // key marks: MSB clear => up to two 31-bit BUFFER-ABSOLUTE byte offsets, resolvable with no scan;
        // MSB set => the frame must be walked to recover keys. Zero means "no keys" - offset 0 can never be
        // a key, because the frame starts '*N\r\n'.
        internal const ulong OverflowFlag = 1UL << 63;
        internal const int SlotBits = 31;
        internal const ulong SlotMask = (1UL << SlotBits) - 1;

        private byte[]? _buffer;
        private readonly int _start;
        private readonly int _length;
        private readonly ulong _keyMarks;

        internal RespFrame(byte[] buffer, int start, int length, int argCount, int slot, ulong keyMarks)
        {
            _buffer = buffer;
            _start = start;
            _length = length;
            _keyMarks = keyMarks;
            ArgCount = argCount;
            Slot = slot;
        }

        /// <summary>The number of RESP arguments, including the command itself.</summary>
        public int ArgCount { get; }

        /// <summary>The combined cluster slot, or <see cref="ServerSelectionStrategy.NoSlot"/>/<see cref="ServerSelectionStrategy.MultipleSlots"/>.</summary>
        public int Slot { get; }

        /// <summary>The rendered frame.</summary>
        public readonly ReadOnlySpan<byte> Span => new(_buffer, _start, _length);

        /// <summary>True when there were more keys than the inline marks can hold, so recovering them needs a walk.</summary>
        public readonly bool KeysNeedScan => (_keyMarks & OverflowFlag) != 0;

        /// <summary>True when no argument was a key.</summary>
        public readonly bool HasNoKeys => _keyMarks == 0;

        /// <summary>
        /// Recover the key payloads without walking the frame. Returns -1 when <see cref="KeysNeedScan"/>,
        /// in which case the caller must walk instead.
        /// </summary>
        public readonly int TryGetKeys(scoped Span<KeyRange> target)
        {
            if ((_keyMarks & OverflowFlag) != 0) return -1;
            var count = 0;
            var a = (int)(_keyMarks & SlotMask);
            var b = (int)((_keyMarks >> SlotBits) & SlotMask);
            if (a != 0) target[count++] = PayloadOf(a);
            if (b != 0) target[count++] = PayloadOf(b);
            return count;
        }

        /// <summary>Resolve a <see cref="KeyRange"/> against the underlying buffer.</summary>
        public readonly ReadOnlySpan<byte> GetKey(in KeyRange range) => new(_buffer, range.Offset, range.Length);

        /// <summary>
        /// Given the buffer-absolute offset of a fragment's '$', parse the self-describing length and return
        /// the payload range; no length needs to be stored alongside the offset.
        /// </summary>
        private readonly KeyRange PayloadOf(int offset)
        {
            var buffer = _buffer!;
            int i = offset + 1, length = 0;
            while (buffer[i] != (byte)'\r')
            {
                length = (length * 10) + (buffer[i] - (byte)'0');
                i++;
            }

            return new KeyRange(i + 2, length);
        }

        public void Dispose()
        {
            var buffer = _buffer;
            _buffer = null;
            if (buffer is not null) ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// EXPERIMENTAL SPIKE. Offset and length of a payload within a <see cref="RespFrame"/>'s buffer.
    /// </summary>
    /// <remarks>
    /// Deliberately not <c>System.Range</c>: down-level that has to be a source polyfill, and the polyfill
    /// must be internal (a public one would collide with the real type on newer targets), so it cannot
    /// appear in API that a consumer might one day see.
    /// </remarks>
    internal readonly struct KeyRange
    {
        public KeyRange(int offset, int length)
        {
            Offset = offset;
            Length = length;
        }

        public int Offset { get; }

        public int Length { get; }
    }
}
