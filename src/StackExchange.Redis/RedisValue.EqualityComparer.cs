using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.IO.Hashing;
using System.Security.Cryptography;
using System.Text;

namespace StackExchange.Redis
{
    public readonly partial struct RedisValue
    {
        /// <summary>
        /// Ways of testing <see cref="RedisValue"/> instances for equality.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Equality only. There is deliberately no ordering counterpart: a total order over
        /// <see cref="RedisValue"/> would have to choose between the numeric, textual and raw-byte readings of
        /// a value, and those disagree for the same logical value. Redis does not define one either - sorted
        /// sets order by score, and <c>ZRANGEBYLEX</c> orders member bytes rather than values.
        /// </para>
        /// <para>
        /// Derivation is closed. Anything wanting its own rules should implement
        /// <see cref="IEqualityComparer{T}"/> directly - it never needed this base class - and keeping the
        /// hierarchy closed leaves room to add members here later without breaking anyone.
        /// </para>
        /// </remarks>
        public abstract class EqualityComparer : IEqualityComparer<RedisValue>, IEqualityComparer
        {
            private protected EqualityComparer() { }

            /// <summary>
            /// Matches <see cref="RedisValue"/>'s own equality, where a blob equals the text it decodes to.
            /// </summary>
            public static EqualityComparer Default { get; } = new DefaultComparer();

            /// <summary>
            /// Compares the UTF-8 form of values without decoding it into text, which is markedly cheaper for
            /// large values.
            /// </summary>
            /// <remarks>
            /// <para>
            /// Agrees with <see cref="Default"/> for text that is well-formed and not numeric. It differs in
            /// three places:
            /// </para>
            /// <list type="bullet">
            /// <item><description>
            /// <b>Numeric text.</b> <see cref="Default"/> reduces anything that parses as a number before
            /// comparing, so <c>"1.0"</c>, <c>"1.00"</c> and <c>"1"</c> are all equal to it, as are <c>"0"</c>
            /// and <c>"-0.0"</c>. This compares the bytes, so they are not - which is how the server
            /// identifies members, and usually what a byte reading is wanted for.
            /// </description></item>
            /// <item><description>
            /// <b>Unpaired surrogates.</b> A string holding one encodes to the same bytes as one holding
            /// U+FFFD, so this calls those equal where <see cref="Default"/> does not.
            /// </description></item>
            /// <item><description>
            /// <b>Non-canonical UTF-8.</b> A blob that does not re-encode to itself - the single byte
            /// <c>0xFF</c>, say, which decodes to U+FFFD - is distinct from that text here, where
            /// <see cref="Default"/> calls them equal.
            /// </description></item>
            /// </list>
            /// <para>
            /// Since both its equality and its hashing read those same bytes, it is self-consistent - which
            /// is what makes the byte reading safe here and not on <see cref="RedisValue"/> itself, whose
            /// <see cref="RedisValue.GetHashCode()"/> hashes the decoded text.
            /// </para>
            /// <para>
            /// It also does not care how a value is stored: the same text compares equal to itself whether it
            /// arrived as a string or as the bytes of that string, which is not true of the default rules for
            /// every input.
            /// </para>
            /// <para>
            /// Hashing is not resistant to deliberate collision-finding - it is chosen for speed, unlike the
            /// framework's string hashing. Do not use it to key on values an untrusted party controls.
            /// </para>
            /// </remarks>
            public static EqualityComparer Binary { get; } = new BinaryComparer();

            /// <inheritdoc/>
            public abstract bool Equals(RedisValue x, RedisValue y);

            /// <inheritdoc/>
            public abstract int GetHashCode(RedisValue obj);

            // The untyped API accepts anything RedisValue itself would accept from object - string, byte[],
            // the numeric types and so on - matching Equals(object) rather than demanding a boxed RedisValue.
            bool IEqualityComparer.Equals(object? x, object? y)
            {
                if (ReferenceEquals(x, y)) return true; // also covers both-null

                var left = TryParse(x, out var leftValid);
                var right = TryParse(y, out var rightValid);
                return leftValid && rightValid && Equals(left, right);
            }

            int IEqualityComparer.GetHashCode(object obj)
            {
                var value = TryParse(obj, out var valid);

                // anything we cannot read is never equal to anything under Equals above, so its own hash is
                // as good as any: it only has to be stable
                return valid ? GetHashCode(value) : obj.GetHashCode();
            }

            private sealed class DefaultComparer : EqualityComparer
            {
                public override bool Equals(RedisValue x, RedisValue y) => x == y;

                public override int GetHashCode(RedisValue obj) => obj.GetHashCode();
            }

            private sealed class BinaryComparer : EqualityComparer
            {
                /// <summary>
                /// Per-process entropy, so hash codes are not predictable between runs. Not from
                /// <see cref="Guid.NewGuid"/>: its version and variant bits are fixed, so the first eight
                /// bytes carry slightly less than they appear to.
                /// </summary>
                private static readonly long Seed = ReadSeed();

                private static long ReadSeed()
                {
                    // the array form rather than Fill(Span<byte>), which the down-level targets lack; this
                    // runs once per process
                    var bytes = new byte[sizeof(long)];
                    using var rng = RandomNumberGenerator.Create();
                    rng.GetBytes(bytes);
                    return BitConverter.ToInt64(bytes, 0);
                }

                private const int StackLimit = 256;

                public override bool Equals(RedisValue x, RedisValue y)
                {
                    if (x.IsNull || y.IsNull) return x.IsNull && y.IsNull;

                    // byte-backed on both sides: the bytes are already there, so no copy is needed
                    if (IsBlob(x.Type) && IsBlob(y.Type)) return BlobSequenceEqual(x, y);

                    // identical text encodes identically, so this is a shortcut rather than a rule; the
                    // unequal case still has to go the byte route, because two different strings can share a
                    // UTF-8 form once unpaired surrogates are involved
                    if (x.Type == StorageType.String && y.Type == StorageType.String
                        && string.Equals(x.RawString(), y.RawString(), StringComparison.Ordinal))
                    {
                        return true;
                    }

                    // A string against a contiguous blob is the case worth caring about: encode the string a
                    // chunk at a time straight onto the blob's own bytes, so a mismatch near the front stops
                    // there instead of after both sides have been written out in full. Deliberately ahead of
                    // any length check - measuring the string's UTF8 length means walking all of it, which
                    // costs more than the comparison usually does.
                    if (x.Type == StorageType.String && IsContiguousBlob(y.Type)) return StringEqualsBytes(x.RawString(), y.UnsafeRawSpan(out _));
                    if (y.Type == StorageType.String && IsContiguousBlob(x.Type)) return StringEqualsBytes(y.RawString(), x.UnsafeRawSpan(out _));

                    int length = x.GetByteCount();
                    if (length != y.GetByteCount()) return false;
                    if (length == 0) return true;

                    byte[]? leasedX = null, leasedY = null;
                    Span<byte> bytesX = length <= StackLimit ? stackalloc byte[StackLimit] : (leasedX = ArrayPool<byte>.Shared.Rent(length));
                    Span<byte> bytesY = length <= StackLimit ? stackalloc byte[StackLimit] : (leasedY = ArrayPool<byte>.Shared.Rent(length));

                    x.CopyTo(bytesX);
                    y.CopyTo(bytesY);
                    bool equal = bytesX.Slice(0, length).SequenceEqual(bytesY.Slice(0, length));

                    if (leasedX is not null) ArrayPool<byte>.Shared.Return(leasedX);
                    if (leasedY is not null) ArrayPool<byte>.Shared.Return(leasedY);
                    return equal;
                }

                private static bool IsContiguousBlob(StorageType type)
                    => type is StorageType.ByteArray or StorageType.MemoryManager or StorageType.ShortBlob;

                /// <summary>
                /// Compares a string's UTF-8 form against bytes, encoding it a chunk at a time so that a
                /// mismatch costs only the chunk that contains it.
                /// </summary>
                private static bool StringEqualsBytes(string s, scoped ReadOnlySpan<byte> utf8)
                {
                    const int ChunkChars = 512;
                    Span<byte> buffer = stackalloc byte[ChunkChars * MaxUtf8BytesPerChar];

                    var chars = s.AsSpan();
                    while (!chars.IsEmpty)
                    {
                        var take = Math.Min(ChunkChars, chars.Length);

                        // Never end a chunk on a high surrogate: the encoder would emit U+FFFD for each half
                        // of a pair split across chunks, which is not what encoding the whole string gives.
                        // Step *back* rather than forward - taking one more character would both overrun the
                        // buffer (513 three-byte characters do not fit in 512*3 bytes) and, where the
                        // character at the boundary is a lone high surrogate, simply move the split onto the
                        // following pair instead of avoiding it. Only reachable when take == ChunkChars, so
                        // it cannot reach zero.
                        if (take < chars.Length && char.IsHighSurrogate(chars[take - 1])) take--;

                        var written = Encoding.UTF8.GetBytes(chars.Slice(0, take), buffer);
                        if (written > utf8.Length || !buffer.Slice(0, written).SequenceEqual(utf8.Slice(0, written))) return false;

                        chars = chars.Slice(take);
                        utf8 = utf8.Slice(written);
                    }
                    return utf8.IsEmpty;
                }

                /// <summary>Worst case UTF-8 bytes for a single char (a lone surrogate becomes U+FFFD).</summary>
                private const int MaxUtf8BytesPerChar = 3;

                public override int GetHashCode(RedisValue obj)
                {
                    if (obj.IsNull) return -1;

                    switch (obj.Type)
                    {
                        case StorageType.ByteArray or StorageType.MemoryManager or StorageType.ShortBlob:
                            return Fold(XxHash3.HashToUInt64(obj.UnsafeRawSpan(out _), Seed));
                        case StorageType.Sequence:
                            var sequence = obj.RawSequence();
                            if (sequence.IsSingleSegment) return Fold(XxHash3.HashToUInt64(sequence.First.Span, Seed));
                            break; // multi-segment: fall through to the copy below rather than streaming
                    }

                    int length = obj.GetByteCount();
                    if (length == 0) return 0;

                    byte[]? leased = null;
                    Span<byte> bytes = length <= StackLimit ? stackalloc byte[StackLimit] : (leased = ArrayPool<byte>.Shared.Rent(length));
                    obj.CopyTo(bytes);
                    var hash = Fold(XxHash3.HashToUInt64(bytes.Slice(0, length), Seed));
                    if (leased is not null) ArrayPool<byte>.Shared.Return(leased);
                    return hash;
                }

                private static int Fold(ulong hash) => unchecked((int)hash ^ (int)(hash >> 32));
            }
        }
    }
}
