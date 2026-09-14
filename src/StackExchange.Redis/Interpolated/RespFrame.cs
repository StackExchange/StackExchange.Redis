using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using RESPite;
using RESPite.Buffers;

namespace StackExchange.Redis.Interpolated
{
    /// <summary>
    /// EXPERIMENTAL SPIKE. A rendered RESP frame, plus the routing and invalidation metadata that was
    /// folded while it was being written.
    /// </summary>
    [Experimental(Experiments.InterpolatedWriter, UrlFormat = Experiments.UrlFormat)]
    public struct RespFrame : IDisposable
    {
        // Key marks, two alternative encodings in one 64-bit field:
        //
        //   MSB clear => up to two 31-bit BUFFER-ABSOLUTE byte offsets, resolvable with no scan. Zero means
        //                "no keys" - offset 0 can never be a key, because the frame starts '*N\r\n'.
        //   MSB set   => a bitmap of the ARGUMENT INDICES that are keys (bits 1..62; argument 0 is always
        //                the command). Resolving those to byte ranges needs a walk of the frame.
        //
        // Bit 0 is free in bitmap mode - argument 0 is the command and can never be a key - so it carries
        // "truncated": there was a key at an argument index above 62 that could not be recorded. Such a
        // frame cannot report its keys at all, and TryGetKeys says so rather than reporting a subset.
        internal const ulong OverflowFlag = 1UL << 63;
        internal const ulong TruncatedFlag = 1UL << 0;
        internal const int MaxBitmapArg = 62;
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
        /// How many arguments were keys, or <c>-1</c> when the frame cannot report them.
        /// </summary>
        /// <remarks>
        /// <b>The one case that returns -1</b> is a key at argument index above
        /// <see cref="MaxBitmapArg"/> (62), which the bitmap has no bit for. It is recorded as a single
        /// "truncated" flag rather than as a partial list, because a partial list is worse than none: a
        /// caller tracking keys for invalidation would believe it had them all. Commands with that many keys
        /// are rare and large; callers should decline to cache such a frame.
        /// </remarks>
        public readonly int KeyCount => KeyCountOf(_keyMarks);

        /// <summary>As <see cref="KeyCount"/>, for a caller holding only the packed marks.</summary>
        internal static int KeyCountOf(ulong keyMarks)
        {
            if ((keyMarks & OverflowFlag) == 0)
            {
                if (keyMarks == 0) return 0;
                return ((keyMarks >> SlotBits) & SlotMask) == 0 ? 1 : 2;
            }

            return (keyMarks & TruncatedFlag) != 0 ? -1 : PopCount(keyMarks & ~OverflowFlag);
        }

        private static int PopCount(ulong value)
        {
#if NET6_0_OR_GREATER
            return System.Numerics.BitOperations.PopCount(value);
#else
            // no BitOperations down-level; this is the standard SWAR popcount
            value -= (value >> 1) & 0x5555555555555555UL;
            value = (value & 0x3333333333333333UL) + ((value >> 2) & 0x3333333333333333UL);
            value = (value + (value >> 4)) & 0x0F0F0F0F0F0F0F0FUL;
            return (int)((value * 0x0101010101010101UL) >> 56);
#endif
        }

        /// <summary>
        /// Recover the key payloads. Returns the number written, or <c>-1</c> if
        /// <paramref name="target"/> is too small or the frame cannot report its keys - see
        /// <see cref="KeyCount"/>, which sizes the buffer and distinguishes the two.
        /// </summary>
        /// <remarks>
        /// One or two keys resolve straight from the stored offsets. More than that resolves from the
        /// argument-index bitmap, which needs a walk of the frame - cheap (the bytes are in L1 and it is
        /// simple length-prefixed skipping) but no longer O(1). Callers that do this per lookup rather than
        /// once per frame should cache the result.
        /// </remarks>
        public readonly int TryGetKeys(scoped Span<KeyRange> target)
            => ResolveKeys(_buffer!, _start, _length, _keyMarks, target);

        /// <summary>
        /// As <see cref="TryGetKeys"/>, for a caller holding the buffer and marks rather than a frame -
        /// which is how <see cref="RespRequest"/> answers the same question after <see cref="Detach"/>.
        /// </summary>
        internal static int ResolveKeys(
            byte[] buffer,
            int start,
            int length,
            ulong keyMarks,
            scoped Span<KeyRange> target)
        {
            if ((keyMarks & OverflowFlag) == 0)
            {
                var count = 0;
                var a = (int)(keyMarks & SlotMask);
                var b = (int)((keyMarks >> SlotBits) & SlotMask);
                var needed = (a != 0 ? 1 : 0) + (b != 0 ? 1 : 0);
                if (target.Length < needed) return -1;
                if (a != 0) target[count++] = PayloadOf(buffer, a);
                if (b != 0) target[count++] = PayloadOf(buffer, b);
                return count;
            }

            if ((keyMarks & TruncatedFlag) != 0) return -1;

            var bitmap = keyMarks & ~OverflowFlag;
            if (target.Length < PopCount(bitmap)) return -1;
            return WalkKeys(buffer, start, length, bitmap, target);
        }

        /// <summary>
        /// Resolve argument indices to payload ranges by walking the frame.
        /// </summary>
        /// <remarks>
        /// A rendered frame is <c>*N\r\n</c> followed by N bulk strings, so this is length-prefixed
        /// skipping - no <c>RespReader</c>, no allocation. Argument 0 is the command, matching the indices
        /// the writer recorded.
        /// </remarks>
        private static int WalkKeys(byte[] buffer, int start, int length, ulong bitmap, scoped Span<KeyRange> target)
        {
            var end = start + length;

            var i = start;
            while (buffer[i] != (byte)'\n') i++; // past the '*N\r\n' header
            i++;

            int arg = 0, count = 0;
            while (i < end)
            {
                var j = i + 1; // past the '$'
                var bulk = 0;
                while (buffer[j] != (byte)'\r')
                {
                    bulk = (bulk * 10) + (buffer[j] - (byte)'0');
                    j++;
                }

                var payload = j + 2;
                if (arg <= MaxBitmapArg && (bitmap & (1UL << arg)) != 0)
                {
                    target[count++] = new KeyRange(payload, bulk);
                }

                i = payload + bulk + 2;
                arg++;
            }

            return count;
        }

        /// <summary>Resolve a <see cref="KeyRange"/> against the underlying buffer.</summary>
        public readonly ReadOnlySpan<byte> GetKey(in KeyRange range) => new(_buffer, range.Offset, range.Length);

        /// <summary>
        /// Given the buffer-absolute offset of a fragment's '$', parse the self-describing length and return
        /// the payload range; no length needs to be stored alongside the offset.
        /// </summary>
        private static KeyRange PayloadOf(byte[] buffer, int offset)
        {
            int i = offset + 1, length = 0;
            while (buffer[i] != (byte)'\r')
            {
                length = (length * 10) + (buffer[i] - (byte)'0');
                i++;
            }

            return new KeyRange(i + 2, length);
        }

        /// <summary>
        /// Hand the rendered bytes over to a reference-counted lease and return a key that can live in a
        /// dictionary. The frame gives up ownership: disposing it afterwards does nothing.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is the point of the whole exercise - the bytes that were about to be sent become the cache
        /// key with no copy, no <c>byte[]</c> and no <c>string</c>. The key is a normal struct rather than a
        /// <c>ref struct</c> precisely so it can be a <c>TKey</c>.
        /// </para>
        /// <para>
        /// The returned key holds ONE reference. Dispose it when done; if it is being stored, take a second
        /// with <see cref="RespRequest.TryRetain"/> and store that.
        /// </para>
        /// <para>
        /// Note the same struct-copy caveat as <see cref="Dispose"/>: this clears ownership on THIS copy of
        /// the frame, so a copy taken earlier still holds the array reference and must not be disposed.
        /// </para>
        /// </remarks>
        public RespRequest Detach(CommandFlags flags = CommandFlags.None)
        {
            var buffer = _buffer ?? throw new ObjectDisposedException(nameof(RespFrame));
            _buffer = null; // ownership moves to the lease
            return new RespRequest(
                buffer,
                RefCountedBuffer.Adopt(buffer, buffer.Length),
                _start,
                _length,
                _keyMarks,
                Slot,
                ArgCount,
                flags);
        }

        /// <summary>
        /// A key that BORROWS this frame's buffer, for probing a cache without taking ownership of anything.
        /// Valid only until the frame is disposed.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is the zero-allocation path, and it is the common one: on a cache HIT the caller never wanted
        /// the buffer, so paying for a lease to find that out is pure waste. <see cref="Detach"/> allocates a
        /// <see cref="RefCountedBuffer"/> - small, but one per lookup, which is exactly the kind of per-call
        /// cost this whole design exists to remove. Measured: 48 bytes a lookup with <see cref="Detach"/>,
        /// zero with this.
        /// </para>
        /// <para>
        /// The returned key cannot be retained and so cannot be stored; call <see cref="Detach"/> on a miss,
        /// when ownership is actually wanted.
        /// </para>
        /// </remarks>
        public RespRequest AsLookupKey(CommandFlags flags = CommandFlags.None)
        {
            var buffer = _buffer ?? throw new ObjectDisposedException(nameof(RespFrame));
            return new RespRequest(
                buffer,
                lease: null,
                _start,
                _length,
                _keyMarks,
                Slot,
                ArgCount,
                flags);
        }

        /// <summary>Return the underlying buffer to the pool; safe to call more than once.</summary>
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
    [Experimental(Experiments.InterpolatedWriter, UrlFormat = Experiments.UrlFormat)]
    public readonly struct KeyRange
    {
        /// <summary>Create a range over a payload within a frame's buffer.</summary>
        /// <param name="offset">Buffer-absolute offset of the payload.</param>
        /// <param name="length">Payload length in bytes.</param>
        public KeyRange(int offset, int length)
        {
            Offset = offset;
            Length = length;
        }

        /// <summary>Buffer-absolute offset of the payload.</summary>
        public int Offset { get; }

        /// <summary>Payload length, in bytes.</summary>
        public int Length { get; }
    }
}
