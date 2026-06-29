using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Buffers.Text;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace StackExchange.Redis
{
    /// <summary>
    /// Represents values that can be stored in redis.
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    public readonly struct RedisValue : IEquatable<RedisValue>, IComparable<RedisValue>, IComparable, IConvertible
    {
        // Maximum payload that fits in an inline short-blob (packed into the overlapped int64 field).
        internal const int MaxInlineBytes = sizeof(long);

        // Prefers an inline (allocation-free) short-blob for <= 8 bytes; otherwise materializes a byte[].
        internal static RedisValue FromRaw(ReadOnlySpan<byte> bytes)
        {
            if (bytes.IsEmpty) return EmptyString;
            if (bytes.Length <= MaxInlineBytes) return new RedisValue(bytes);
            return bytes.ToArray();
        }

        internal static readonly RedisValue[] EmptyArray = Array.Empty<RedisValue>();

#pragma warning disable SA1134
        [FieldOffset(0)] private readonly int _index;
        [FieldOffset(4)] private readonly int _length;

        // these should only be used if the value of _obj is the appropriate sentinel
        [FieldOffset(0)] private readonly long _valueInt64;
        [FieldOffset(0)] private readonly ulong _valueUInt64;
        [FieldOffset(0)] private readonly double _valueDouble;

        [FieldOffset(8)] private readonly object? _obj;
#pragma warning restore SA1134

        private RedisValue(byte[]? value)
        {
            if (value is null)
            {
                this = default;
            }
            else if (value.Length == 0)
            {
                this = EmptyString;
            }
            else
            {
                Unsafe.SkipInit(out this);
                _index = 0;
                _length = value.Length;
                _obj = value;
            }
        }

        // inline short-blob (1..8 bytes): bytes are packed into the overlapped _valueInt64 field, length is
        // carried by the ShortBlob sentinel. Read/write the bytes via the raw memory layout (MemoryMarshal)
        // so it is endianness-agnostic - we never interpret _valueInt64 as a number for this kind.
        private unsafe RedisValue(ReadOnlySpan<byte> shortBlob)
        {
            Debug.Assert(shortBlob.Length is > 0 and <= ShortBlob.MaxLength, "short-blob length out of range");
            Unsafe.SkipInit(out this);
            long packed = 0; // zero so the unused high bytes are deterministic
            shortBlob.CopyTo(new Span<byte>(Unsafe.AsPointer(ref packed), sizeof(long)));
            _valueInt64 = packed;
            _obj = ShortBlob.For(shortBlob.Length);
        }

        private RedisValue(ReadOnlyMemory<byte> value)
        {
            Unsafe.SkipInit(out this);
            if (value.IsEmpty)
            {
                this = EmptyString;
            }
            else if (MemoryMarshal.TryGetArray(value, out var segment))
            {
                _index = segment.Offset;
                _length = segment.Count;
                _obj = segment.Array;
            }
            else if (MemoryMarshal.TryGetMemoryManager<byte, MemoryManager<byte>>(value, out var manager, out var index, out var length))
            {
                _index = index;
                _length = length;
                _obj = manager;
            }
            else
            {
                Throw();
                static void Throw() => throw new ArgumentException("Unrecognized memory type");
            }
        }

        private RedisValue(long value)
        {
            Unsafe.SkipInit(out this);
            _valueInt64 = value;
            _obj = Sentinel_SignedInteger;
        }

        private RedisValue(ulong value)
        {
            Unsafe.SkipInit(out this);
            if (value <= long.MaxValue)
            {
                _valueInt64 = (long)value;
                _obj = Sentinel_SignedInteger;
            }
            else
            {
                _valueUInt64 = value;
                _obj = Sentinel_UnsignedInteger;
            }
        }

        private RedisValue(double value)
        {
            Unsafe.SkipInit(out this);
            try
            {
                var i64 = (long)value;
                // note: double doesn't offer integer accuracy at 64 bits, so we know it can't be unsigned (only use that for 64-bit)
                // ReSharper disable once CompareOfFloatsByEqualityOperator
                if (value == i64)
                {
                    _valueInt64 = i64;
                    _obj = Sentinel_SignedInteger;
                    return;
                }
            }
            catch
            {
                // ignored
            }

            _valueDouble = value;
            _obj = Sentinel_Double;
        }

        /// <summary>
        /// Creates a <see cref="RedisValue"/> from a string.
        /// </summary>
        public RedisValue(string value)
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (value is null)
            {
                // I have trust issues
                this = default;
            }
            else
            {
                Unsafe.SkipInit(out this);
                _index = 0;
                _length = value.Length;
                _obj = value;
            }
        }

#pragma warning disable RCS1085 // use auto-prop
        // ReSharper disable ConvertToAutoProperty
        internal double OverlappedValueDouble => _valueDouble;

        internal long OverlappedValueInt64 => _valueInt64;

        internal ulong OverlappedValueUInt64 => _valueUInt64;
        // ReSharper restore ConvertToAutoProperty
#pragma warning restore RCS1085 // use auto-prop

        private static readonly object Sentinel_SignedInteger = new();
        private static readonly object Sentinel_UnsignedInteger = new();
        private static readonly object Sentinel_Double = new();

        /// <summary>
        /// Obtain this value as an object - to be used alongside Unbox.
        /// </summary>
        public object? Box()
        {
            var obj = _obj;
            if (obj is null || obj is string || (obj is byte[] b && _index == 0 && _length == b.Length)) return obj;
            if (obj == Sentinel_SignedInteger)
            {
                var l = OverlappedValueInt64;
                if (l >= -1 && l <= 20) return s_CommonInt32[((int)l) + 1];
                return l;
            }
            if (obj == Sentinel_UnsignedInteger)
            {
                return OverlappedValueUInt64;
            }
            if (obj == Sentinel_Double)
            {
                var d = OverlappedValueDouble;
                if (double.IsPositiveInfinity(d)) return s_DoublePosInf;
                if (double.IsNegativeInfinity(d)) return s_DoubleNegInf;
                if (double.IsNaN(d)) return s_DoubleNAN;
                return d;
            }
            return this;
        }

        /// <summary>
        /// Parse this object as a value - to be used alongside Box.
        /// </summary>
        /// <param name="value">The value to unbox.</param>
        public static RedisValue Unbox(object? value)
        {
            var val = TryParse(value, out var valid);
            if (!valid) throw new ArgumentException("Could not parse value", nameof(value));
            return val;
        }

        /// <summary>
        /// Represents the string <c>""</c>.
        /// </summary>
        public static RedisValue EmptyString { get; } = new("");

        // note: it is *really important* that this s_EmptyString assignment happens *after* the EmptyString initializer above!
        private static readonly object s_DoubleNAN = double.NaN, s_DoublePosInf = double.PositiveInfinity, s_DoubleNegInf = double.NegativeInfinity,
            s_EmptyString = RedisValue.EmptyString;
        private static readonly object[] s_CommonInt32 = Enumerable.Range(-1, 22).Select(i => (object)i).ToArray(); // [-1,20] = 22 values

        /// <summary>
        /// A null value.
        /// </summary>
        public static RedisValue Null { get; } = default;

        /// <summary>
        /// Indicates whether the **underlying** value is a primitive integer (signed or unsigned); this is **not**
        /// the same as whether the value can be *treated* as an integer - see <seealso cref="TryParse(out int)"/>
        /// and <seealso cref="TryParse(out long)"/>, which is usually the more appropriate test.
        /// </summary>
        [Browsable(false), EditorBrowsable(EditorBrowsableState.Advanced)] // hide it, because this *probably* isn't what callers need
        public bool IsInteger => _obj == Sentinel_SignedInteger || _obj == Sentinel_UnsignedInteger;

        /// <summary>
        /// Indicates whether the value should be considered a null value.
        /// </summary>
        public bool IsNull => _obj is null;

        /// <summary>
        /// Indicates whether the value is either null or a zero-length value.
        /// </summary>
        public bool IsNullOrEmpty
        {
            get
            {
                // primitives are never null; a short-blob is by construction always 1..8 bytes (and its
                // _length field is unusable anyway, as it overlaps the inline bytes)
                if (_obj == Sentinel_Double | _obj == Sentinel_SignedInteger | _obj == Sentinel_UnsignedInteger | _obj is ShortBlob) return false;
                // everything else either null or a buffer or some kind; can use length
                return _length == 0;
            }
        }

        /// <summary>
        /// Indicates whether the value is greater than zero-length or has an integer value.
        /// </summary>
        public bool HasValue => !IsNullOrEmpty;

        /// <summary>
        /// Indicates whether two RedisValue values are equivalent.
        /// </summary>
        /// <param name="x">The first <see cref="RedisValue"/> to compare.</param>
        /// <param name="y">The second <see cref="RedisValue"/> to compare.</param>
        public static bool operator !=(RedisValue x, RedisValue y) => !(x == y);

        private static ReadOnlySequence<byte> GetSequence(ReadOnlySequenceSegment<byte> startSegment, int startIndex, int length)
        {
            var endIndex = length - (startSegment.Memory.Length - startIndex);
            var endSegment = startSegment;
            do
            {
                endSegment = endSegment.Next ?? throw new InvalidOperationException("EndSegment is null");
                var len = endSegment.Memory.Length;
                if (endIndex <= len) break;
                endIndex -= len;
            }
            while (true);
            return new ReadOnlySequence<byte>(startSegment, startIndex, endSegment, endIndex);
        }

        internal ReadOnlySequenceSegmentIterator<byte> RawSequenceIterator()
        {
            if (_obj is ReadOnlySequenceSegment<byte> s) return new(s, _index, _length);
            ThrowRawType();
            return default;
        }

        private ReadOnlySequence<byte> RawSequence()
        {
            if (_obj is ReadOnlySequenceSegment<byte> s) return GetSequence(s, _index, _length);
            if (_obj is byte[] a) return new(a, _index, _length);
            if (_obj is MemoryManager<byte> m) return new(m.Memory.Slice(_index, _length));
            ThrowRawType();
            return default;
        }

        // Linearizes a Sequence payload into the supplied buffer (which must be at least _length long),
        // walking the segments directly via the iterator - i.e. without paying to build a ReadOnlySequence -
        // and returns the populated portion of the buffer.
        private ReadOnlySpan<byte> CopyRawSequence(Span<byte> destination)
        {
            var iterator = RawSequenceIterator();
            int offset = 0;
            while (iterator.TryNext(out var memory))
            {
                memory.Span.CopyTo(destination.Slice(offset));
                offset += memory.Length;
            }
            Debug.Assert(offset == _length, "linearized length mismatch");
            return destination.Slice(0, offset);
        }

        // Returns a span over the bytes of any contiguous-blob kind (ByteArray/MemoryManager/ShortBlob).
        // For a ShortBlob the bytes are unpacked into 'stackStorage', so the caller MUST keep 'stackStorage'
        // alive (it must be a genuine stack local) for as long as it uses the returned span - the span aliases
        // that slot. For heap blobs 'stackStorage' is left untouched and the span points at the heap, so a
        // discard ('out _') is always fine there.
        //
        // "Unsafe" because the contract is unstated in the type system: on older TFMs the ShortBlob span is
        // built over a *raw* pointer to 'stackStorage', so passing a ref to a movable location (e.g. a field
        // on a heap object) is undefined behaviour - the GC may relocate it out from under the span. On NET
        // we keep a managed pointer throughout (CreateReadOnlySpan), which the GC tracks, removing that hazard.
        internal
#if !NET
        unsafe
#endif
        ReadOnlySpan<byte> UnsafeRawSpan(out long stackStorage)
        {
            if (_obj is ShortBlob sb)
            {
                stackStorage = _valueInt64;
#if NET
                return MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<long, byte>(ref stackStorage), sb.Length);
#else
                return new ReadOnlySpan<byte>(Unsafe.AsPointer(ref stackStorage), sb.Length);
#endif
            }
            // heap path: 'stackStorage' is unused, so skip the redundant zero-init
            Unsafe.SkipInit(out stackStorage);
            if (_obj is byte[] b) return new ReadOnlySpan<byte>(b, _index, _length);
            if (_obj is MemoryManager<byte> m) return m.GetSpan().Slice(_index, _length);
            ThrowRawType();
            return default;
        }

        // logical byte length of any contiguous-blob kind (ByteArray/MemoryManager/ShortBlob)
        private int BlobLength => _obj is ShortBlob sb ? sb.Length : _length;

        // true for the byte-backed storage kinds (everything that compares "by bytes")
        private static bool IsBlob(StorageType type)
            => type is StorageType.ByteArray or StorageType.MemoryManager or StorageType.ShortBlob or StorageType.Sequence;

        // byte-wise equality between any two byte-backed values, in any combination of contiguous/sequence
        private static bool BlobSequenceEqual(in RedisValue x, in RedisValue y)
        {
            // at most two stack slots back the inline short-blobs; reuse named locals rather than relying
            // on the compiler to coalesce per-call 'out _' temps
            long xScratch, yScratch;
            if (x.Type == StorageType.Sequence)
            {
                return y.Type == StorageType.Sequence
                    ? x.RawSequence().SequenceEqual(y.RawSequence())
                    : x.RawSequence().SequenceEqual(y.UnsafeRawSpan(out yScratch));
            }
            if (y.Type == StorageType.Sequence)
            {
                return y.RawSequence().SequenceEqual(x.UnsafeRawSpan(out xScratch));
            }
            return x.UnsafeRawSpan(out xScratch).SequenceEqual(y.UnsafeRawSpan(out yScratch));
        }

        // byte-wise ordinal comparison between any two byte-backed values, in any combination of
        // contiguous (ByteArray/MemoryManager/ShortBlob) and multi-segment (Sequence)
        private static int BlobCompareTo(in RedisValue x, in RedisValue y)
        {
            long xScratch, yScratch; // at most two stack slots; reuse named locals (see BlobSequenceEqual)
            var xSeq = x.Type == StorageType.Sequence;
            var ySeq = y.Type == StorageType.Sequence;
            if (xSeq && ySeq) return x.RawSequence().SequenceCompareTo(y.RawSequence());
            if (xSeq) return x.RawSequence().SequenceCompareTo(y.UnsafeRawSpan(out yScratch));
            if (ySeq) return -y.RawSequence().SequenceCompareTo(x.UnsafeRawSpan(out xScratch)); // negate: computed y vs x
            return x.UnsafeRawSpan(out xScratch).SequenceCompareTo(y.UnsafeRawSpan(out yScratch));
        }

        // true if 'whole' starts with the bytes of 'prefix', for any combination of byte-backed kinds
        private static bool BlobStartsWith(in RedisValue whole, in RedisValue prefix)
        {
            long wScratch, pScratch; // at most two stack slots; reuse named locals (see BlobSequenceEqual)
            var wSeq = whole.Type == StorageType.Sequence;
            var pSeq = prefix.Type == StorageType.Sequence;
            if (wSeq && pSeq) return whole.RawSequence().StartsWith(prefix.RawSequence());
            if (wSeq) return whole.RawSequence().StartsWith(prefix.UnsafeRawSpan(out pScratch));
            if (pSeq) return whole.UnsafeRawSpan(out wScratch).StartsWith(prefix.RawSequence());
            return whole.UnsafeRawSpan(out wScratch).StartsWith(prefix.UnsafeRawSpan(out pScratch));
        }

        internal string RawString()
        {
            if (_obj is string s) return s;
            ThrowRawType();
            return "";
        }

        private static void ThrowRawType() => throw new InvalidOperationException("Invalid raw operation.");

        /// <summary>
        /// Indicates whether two RedisValue values are equivalent.
        /// </summary>
        /// <param name="x">The first <see cref="RedisValue"/> to compare.</param>
        /// <param name="y">The second <see cref="RedisValue"/> to compare.</param>
        public static bool operator ==(RedisValue x, RedisValue y)
        {
            x = x.Simplify();
            y = y.Simplify();
            StorageType xType = x.Type, yType = y.Type;

            if (xType == StorageType.Null) return yType == StorageType.Null;
            if (yType == StorageType.Null) return false;

            if (xType == yType)
            {
                switch (xType)
                {
                    case StorageType.Double: // make sure we use double equality rules
                        // ReSharper disable once CompareOfFloatsByEqualityOperator
                        return x.OverlappedValueDouble == y.OverlappedValueDouble;
                    case StorageType.Int64:
                    case StorageType.UInt64: // as long as xType == yType, only need to check the bits
                        return x._valueInt64 == y._valueInt64;
                    case StorageType.String:
                        return x.RawString() == y.RawString();
                    case StorageType.ByteArray or StorageType.MemoryManager or StorageType.ShortBlob or StorageType.Sequence:
                        return BlobSequenceEqual(x, y);
                }
            }

            // if either is a numeric type, and the other isn't the *same* type (above), then:
            // it can't be equal
            switch (xType)
            {
                case StorageType.UInt64:
                case StorageType.Int64:
                case StorageType.Double:
                    return false;
            }
            switch (yType)
            {
                case StorageType.UInt64:
                case StorageType.Int64:
                case StorageType.Double:
                    return false;
            }

            // both are non-null, non-numeric, and of different kinds; if both are byte-backed (byte[] /
            // memory / short-blob / sequence) compare by raw bytes in any combination
            if (IsBlob(xType) && IsBlob(yType)) return BlobSequenceEqual(x, y);

            // otherwise (anything involving a string), compare as strings
            return (string?)x == (string?)y;
        }

        /// <summary>
        /// See <see cref="object.Equals(object)"/>.
        /// </summary>
        /// <param name="obj">The other <see cref="RedisValue"/> to compare.</param>
        public override bool Equals(object? obj)
        {
            if (obj == null) return IsNull;
            if (obj is RedisValue typed) return Equals(typed);
            var other = TryParse(obj, out var valid);
            return valid && this == other; // can't be equal if parse fail
        }

        /// <summary>
        /// Indicates whether two RedisValue values are equivalent.
        /// </summary>
        /// <param name="other">The <see cref="RedisValue"/> to compare to.</param>
        public bool Equals(RedisValue other) => this == other;

        /// <inheritdoc/>
        public override int GetHashCode() => GetHashCode(this);
        private static int GetHashCode(RedisValue x)
        {
            x = x.Simplify();
            switch (x.Type)
            {
                case StorageType.Null:
                    return -1;
                case StorageType.Double:
                    return x.OverlappedValueDouble.GetHashCode();
                case StorageType.Int64 or StorageType.UInt64:
                    return x._valueInt64.GetHashCode();
                case StorageType.String:
                    return x.RawString().GetHashCode();
            }

            // Everything else - byte/memory/sequence buffers - compares to each other (and to strings) "as
            // strings" (see operator ==): e.g. "inf" the bytes equals "inf" the string. Anything that looked
            // numeric was already reduced to Int64/Double by Simplify() above, so the equality-consistent
            // hash for what remains is the hash of the string form. (We must NOT hash raw bytes: that would
            // give byte buffers a different hash from the equal string.)
#if NET
            // hash the decoded UTF8 chars directly, which avoids allocating a transient string; this matches
            // string.GetHashCode() for the equivalent text
            const int StackLimit = 256;
            var maxChars = x.GetMaxCharCount();
            char[]? leased = null;
            Span<char> chars = maxChars <= StackLimit ? stackalloc char[StackLimit] : (leased = ArrayPool<char>.Shared.Rent(maxChars));
            var written = x.CopyTo(chars);
            var hashCode = string.GetHashCode(chars.Slice(0, written));
            if (leased is not null) ArrayPool<char>.Shared.Return(leased);
            return hashCode;
#else
            // no string.GetHashCode(ReadOnlySpan<char>) on these targets, so fall back to the string form
            return ((string)x!).GetHashCode();
#endif
        }

        /// <summary>
        /// Returns a string representation of the value.
        /// </summary>
        public override string ToString() => (string?)this ?? string.Empty;

        internal static unsafe bool Equals(byte[]? x, byte[]? y)
        {
            if ((object?)x == (object?)y) return true; // ref equals
            if (x == null || y == null) return false;
            int len = x.Length;
            if (len != y.Length) return false;

            int octets = len / 8, spare = len % 8;
            fixed (byte* x8 = x, y8 = y)
            {
                long* x64 = (long*)x8, y64 = (long*)y8;
                for (int i = 0; i < octets; i++)
                {
                    if (x64[i] != y64[i]) return false;
                }
                int offset = len - spare;
                while (spare-- != 0)
                {
                    if (x8[offset] != y8[offset++]) return false;
                }
            }
            return true;
        }

        private static int AddHashCode(ReadOnlySpan<byte> span, int acc)
        {
            unchecked
            {
                int len = span.Length;
                Debug.Assert(len > 0);

                var span64 = MemoryMarshal.Cast<byte, long>(span);
                for (int i = 0; i < span64.Length; i++)
                {
                    var val = span64[i];
                    int valHash = ((int)val) ^ ((int)(val >> 32));
                    acc = ((acc << 5) + acc) ^ valHash;
                }
                int spare = len % 8, offset = len - spare;
                while (spare-- != 0)
                {
                    acc = ((acc << 5) + acc) ^ span[offset++];
                }
                return acc;
            }
        }

        // used by RedisKey, whose equality is byte-based (unlike RedisValue, which treats non-numeric
        // buffers as strings - see GetHashCode(RedisValue))
        internal static int GetHashCode(ReadOnlySpan<byte> span)
        {
            if (span.Length == 0) return 0;

            return AddHashCode(span, HashCodeStart);
        }

        private const int HashCodeStart = 728271210;

        internal void AssertNotNull()
        {
            if (IsNull) throw new ArgumentException("A null value is not valid in this context");
        }

        internal enum StorageType
        {
            Null,
            Int64,
            UInt64,
            Double,
            MemoryManager,
            ByteArray,
            String,
            Sequence,
            ShortBlob,
            Unknown,
        }

        // Sentinel for inline blobs of 1..8 bytes: the bytes live directly in the overlapped _valueInt64
        // field (so _index/_length are NOT usable - the length comes from this sentinel instead). This lets
        // short payloads - most literals, short keys/values, and inbound DB strings - avoid a byte[] alloc.
        private sealed class ShortBlob
        {
            internal const int MaxLength = MaxInlineBytes;
            private ShortBlob(int length) => Length = length;
            internal int Length { get; }
            // instances for lengths 1..8 only; length 0 is always represented as EmptyString, never a
            // ShortBlob - so a ShortBlob is, by construction, never null or empty
            private static readonly ShortBlob[] s_byLength =
            {
                new(1), new(2), new(3), new(4), new(5), new(6), new(7), new(8),
            };
            internal static ShortBlob For(int length)
            {
                Debug.Assert(length is >= 1 and <= MaxLength, "short-blob length out of range");
                return s_byLength[length - 1];
            }
        }

        internal StorageType Type
        {
            get
            {
                var obj = _obj;
                if (obj is null) return StorageType.Null;
                if (obj == Sentinel_SignedInteger) return StorageType.Int64;
                if (obj == Sentinel_Double) return StorageType.Double;
                if (obj is string) return StorageType.String;
                // short blobs are expected to be very common on the inbound/read path (most small values
                // and keys are <= 8 bytes), so probe for them early
                if (obj is ShortBlob) return StorageType.ShortBlob;
                if (obj is byte[]) return StorageType.ByteArray;
                if (obj == Sentinel_UnsignedInteger) return StorageType.UInt64;
                if (obj is MemoryManager<byte>) return StorageType.MemoryManager;
                if (obj is ReadOnlySequenceSegment<byte>) return StorageType.Sequence;
                return StorageType.Unknown;
            }
        }

        // used in the toy server only!
        internal static RedisValue CreateForeign<T>(T value, int index, int length) where T : class
        {
            if (typeof(T) == typeof(string) || typeof(T) == typeof(byte[])) Throw();
            return new RedisValue(value, index, length);
            static void Throw() => throw new InvalidOperationException();
        }

        private RedisValue(object obj, int index, int length)
        {
            Unsafe.SkipInit(out this);
            _index = index;
            _length = length;
            _obj = obj;
        }

        // used in the toy server only!
        internal bool TryGetForeign<T>([NotNullWhen(true)] out T? value, out int index, out int length)
            where T : class
        {
            if (typeof(T) != typeof(string) && typeof(T) != typeof(byte[]) && _obj is T found)
            {
                index = _index;
                length = _length;
                value = found;
                return true;
            }
            value = null;
            index = 0;
            length = 0;
            return false;
        }

        /// <summary>
        /// Get the size of this value in bytes.
        /// </summary>
        public long Length() => Type switch
        {
            StorageType.Null => 0,
            StorageType.MemoryManager or StorageType.ByteArray or StorageType.Sequence or StorageType.ShortBlob => BlobLength,
            StorageType.String => Encoding.UTF8.GetByteCount(RawString()),
            StorageType.Int64 => Format.MeasureInt64(OverlappedValueInt64),
            StorageType.UInt64 => Format.MeasureUInt64(OverlappedValueUInt64),
            StorageType.Double => Format.MeasureDouble(OverlappedValueDouble),
            _ => throw new InvalidOperationException("Unable to compute length of type: " + Type),
        };

        /// <summary>
        /// Compare against a RedisValue for relative order.
        /// </summary>
        /// <param name="other">The other <see cref="RedisValue"/> to compare.</param>
        public int CompareTo(RedisValue other) => CompareTo(this, other);

        private static int CompareTo(RedisValue x, RedisValue y)
        {
            try
            {
                x = x.Simplify();
                y = y.Simplify();
                StorageType xType = x.Type, yType = y.Type;

                if (xType == StorageType.Null) return yType == StorageType.Null ? 0 : -1;
                if (yType == StorageType.Null) return 1;

                if (xType == yType)
                {
                    switch (xType)
                    {
                        case StorageType.Double:
                            return x.OverlappedValueDouble.CompareTo(y.OverlappedValueDouble);
                        case StorageType.Int64:
                            return x.OverlappedValueInt64.CompareTo(y.OverlappedValueInt64);
                        case StorageType.UInt64:
                            return x.OverlappedValueUInt64.CompareTo(y.OverlappedValueUInt64);
                        case StorageType.String:
                            return string.CompareOrdinal(x.RawString(), y.RawString());
                        case StorageType.MemoryManager or StorageType.ByteArray or StorageType.ShortBlob or StorageType.Sequence:
                            return BlobCompareTo(x, y);
                    }
                }

                switch (xType)
                { // numbers can be still be compared between types
                    case StorageType.Double:
                        if (yType == StorageType.Int64) return x.OverlappedValueDouble.CompareTo((double)y.OverlappedValueInt64);
                        if (yType == StorageType.UInt64) return x.OverlappedValueDouble.CompareTo((double)y.OverlappedValueUInt64);
                        break;
                    case StorageType.Int64:
                        if (yType == StorageType.Double) return ((double)x.OverlappedValueInt64).CompareTo(y.OverlappedValueDouble);
                        if (yType == StorageType.UInt64) return 1; // we only use unsigned if > int64, so: y is bigger
                        break;
                    case StorageType.UInt64:
                        if (yType == StorageType.Double) return ((double)x.OverlappedValueUInt64).CompareTo(y.OverlappedValueDouble);
                        if (yType == StorageType.Int64) return -1; // we only use unsigned if > int64, so: x is bigger
                        break;
                    case StorageType.MemoryManager or StorageType.ByteArray or StorageType.ShortBlob or StorageType.Sequence
                        when IsBlob(yType):
                        return BlobCompareTo(x, y);
                }

                // otherwise, compare as strings
                return string.CompareOrdinal((string?)x, (string?)y);
            }
            catch (Exception ex)
            {
                ConnectionMultiplexer.TraceWithoutContext(ex.Message);
            }
            // if all else fails, consider equivalent
            return 0;
        }

        int IComparable.CompareTo(object? obj)
        {
            if (obj == null) return CompareTo(Null);

            var val = TryParse(obj, out var valid);
            if (!valid) return -1; // parse fail

            return CompareTo(val);
        }

        internal static RedisValue TryParse(object? obj, out bool valid)
        {
            valid = true;
            switch (obj)
            {
                case null: return Null;
                case string v: return v;
                case int v: return v;
                case uint v: return v;
                case double v: return v;
                case byte[] v: return v;
                case bool v: return v;
                case long v: return v;
                case ulong v: return v;
                case float v: return v;
                case ReadOnlyMemory<byte> v: return v;
                case Memory<byte> v: return v;
                case RedisValue v: return v;
                default:
                    valid = false;
                    return Null;
            }
        }

        /// <summary>
        /// Creates a new <see cref="RedisValue"/> from an <see cref="int"/>.
        /// </summary>
        /// <param name="value">The <see cref="int"/> to convert to a <see cref="RedisValue"/>.</param>
        public static implicit operator RedisValue(int value) => new(value);

        /// <summary>
        /// Creates a new <see cref="RedisValue"/> from an <see cref="T:Nullable{int}"/>.
        /// </summary>
        /// <param name="value">The <see cref="T:Nullable{int}"/> to convert to a <see cref="RedisValue"/>.</param>
        public static implicit operator RedisValue(int? value) => value == null ? Null : new(value.GetValueOrDefault());

        /// <summary>
        /// Creates a new <see cref="RedisValue"/> from an <see cref="long"/>.
        /// </summary>
        /// <param name="value">The <see cref="long"/> to convert to a <see cref="RedisValue"/>.</param>
        public static implicit operator RedisValue(long value) => new(value);

        /// <summary>
        /// Creates a new <see cref="RedisValue"/> from an <see cref="T:Nullable{long}"/>.
        /// </summary>
        /// <param name="value">The <see cref="T:Nullable{long}"/> to convert to a <see cref="RedisValue"/>.</param>
        public static implicit operator RedisValue(long? value) => value == null ? Null : new(value.GetValueOrDefault());

        /// <summary>
        /// Creates a new <see cref="RedisValue"/> from an <see cref="ulong"/>.
        /// </summary>
        /// <param name="value">The <see cref="ulong"/> to convert to a <see cref="RedisValue"/>.</param>
        [CLSCompliant(false)]
        public static implicit operator RedisValue(ulong value) => new(value);

        /// <summary>
        /// Creates a new <see cref="RedisValue"/> from an <see cref="T:Nullable{ulong}"/>.
        /// </summary>
        /// <param name="value">The <see cref="T:Nullable{ulong}"/> to convert to a <see cref="RedisValue"/>.</param>
        [CLSCompliant(false)]
        public static implicit operator RedisValue(ulong? value) => value == null ? Null : new(value.GetValueOrDefault());

        /// <summary>
        /// Creates a new <see cref="RedisValue"/> from an <see cref="uint"/>.
        /// </summary>
        /// <param name="value">The <see cref="uint"/> to convert to a <see cref="RedisValue"/>.</param>
        [CLSCompliant(false)]
        public static implicit operator RedisValue(uint value) => new(value);

        /// <summary>
        /// Creates a new <see cref="RedisValue"/> from an <see cref="T:Nullable{uint}"/>.
        /// </summary>
        /// <param name="value">The <see cref="T:Nullable{uint}"/> to convert to a <see cref="RedisValue"/>.</param>
        [CLSCompliant(false)]
        public static implicit operator RedisValue(uint? value) => value == null ? Null : new(value.GetValueOrDefault());

        /// <summary>
        /// Creates a new <see cref="RedisValue"/> from an <see cref="double"/>.
        /// </summary>
        /// <param name="value">The <see cref="double"/> to convert to a <see cref="RedisValue"/>.</param>
        public static implicit operator RedisValue(double value) => new(value);

        /// <summary>
        /// Creates a new <see cref="RedisValue"/> from an <see cref="T:Nullable{double}"/>.
        /// </summary>
        /// <param name="value">The <see cref="T:Nullable{double}"/> to convert to a <see cref="RedisValue"/>.</param>
        public static implicit operator RedisValue(double? value) => value == null ? Null : (RedisValue)value.GetValueOrDefault();

        /// <summary>
        /// Creates a new <see cref="RedisValue"/> from a <see cref="T:ReadOnlyMemory{byte}"/>.
        /// </summary>
        /// <param name="value">The <see cref="T:ReadOnlyMemory{byte}"/> to convert to a <see cref="RedisValue"/>.</param>
        public static implicit operator RedisValue(ReadOnlyMemory<byte> value) => new(value);

        /// <summary>
        /// Creates a new <see cref="RedisValue"/> from a <see cref="T:ReadOnlySequence{byte}"/>.
        /// </summary>
        /// <param name="value">The <see cref="T:ReadOnlySequence{byte}"/> to cast to a <see cref="RedisValue"/>.</param>
        public static implicit operator RedisValue(ReadOnlySequence<byte> value)
        {
            if (value.IsSingleSegment) return new(value.First);
            // what is the maximum length? Array.MaxLength? 512MB?
            if (value.Length > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(value));

            var pos = value.Start;
            var segment = pos.GetObject() ?? throw new InvalidOperationException("StartSegment is null");
            return new((ReadOnlySequenceSegment<byte>)segment, pos.GetInteger(), checked((int)value.Length));
        }

        /// <summary>
        /// Creates a new <see cref="RedisValue"/> from a <see cref="T:Memory{byte}"/>.
        /// </summary>
        /// <param name="value">The <see cref="T:Memory{byte}"/> to convert to a <see cref="RedisValue"/>.</param>
        public static implicit operator RedisValue(Memory<byte> value) => new(value);

        /// <summary>
        /// Creates a new <see cref="RedisValue"/> from an <see cref="string"/>.
        /// </summary>
        /// <param name="value">The <see cref="string"/> to convert to a <see cref="RedisValue"/>.</param>
        public static implicit operator RedisValue(string? value) => value is null ? Null : new(value);

        /// <summary>
        /// Creates a new <see cref="RedisValue"/> from an <see cref="T:byte[]"/>.
        /// </summary>
        /// <param name="value">The <see cref="T:byte[]"/> to convert to a <see cref="RedisValue"/>.</param>
        public static implicit operator RedisValue(byte[]? value) => new(value);

        /// <summary>
        /// Creates a new <see cref="RedisValue"/> from an <see cref="bool"/>.
        /// </summary>
        /// <param name="value">The <see cref="bool"/> to convert to a <see cref="RedisValue"/>.</param>
        public static implicit operator RedisValue(bool value) => new RedisValue(value ? 1 : 0);

        /// <summary>
        /// Creates a new <see cref="RedisValue"/> from an <see cref="T:Nullable{bool}"/>.
        /// </summary>
        /// <param name="value">The <see cref="T:Nullable{bool}"/> to convert to a <see cref="RedisValue"/>.</param>
        public static implicit operator RedisValue(bool? value) => value == null ? Null
            : new(value.GetValueOrDefault() ? 1 : 0);

        /// <summary>
        /// Converts a <see cref="RedisValue"/> to a <see cref="bool"/>.
        /// </summary>
        /// <param name="value">The <see cref="RedisValue"/> to convert.</param>
        public static explicit operator bool(RedisValue value) => (long)value switch
        {
            0 => false,
            1 => true,
            _ => throw new InvalidCastException(),
        };

        /// <summary>
        /// Converts a <see cref="RedisValue"/> to a <see cref="int"/>.
        /// </summary>
        /// <param name="value">The <see cref="RedisValue"/> to convert.</param>
        public static explicit operator int(RedisValue value)
            => checked((int)(long)value);

        /// <summary>
        /// Converts a <see cref="RedisValue"/> to a <see cref="long"/>.
        /// </summary>
        /// <param name="value">The <see cref="RedisValue"/> to convert.</param>
        public static explicit operator long(RedisValue value)
        {
            value = value.Simplify();
            return value.Type switch
            {
                StorageType.Null => 0, // in redis, an arithmetic zero is kinda the same thing as not-exists (think "incr")
                StorageType.Int64 => value.OverlappedValueInt64,
                StorageType.UInt64 => checked((long)value.OverlappedValueUInt64), // this will throw since unsigned is always 64-bit
                _ => throw new InvalidCastException($"Unable to cast from {value.Type} to long: '{value}'"),
            };
        }

        /// <summary>
        /// Converts a <see cref="RedisValue"/> to a <see cref="uint"/>.
        /// </summary>
        /// <param name="value">The <see cref="RedisValue"/> to convert.</param>
        [CLSCompliant(false)]
        public static explicit operator uint(RedisValue value)
        {
            value = value.Simplify();
            return value.Type switch
            {
                StorageType.Null => 0, // in redis, an arithmetic zero is kinda the same thing as not-exists (think "incr")
                StorageType.Int64 => checked((uint)value.OverlappedValueInt64),
                StorageType.UInt64 => checked((uint)value.OverlappedValueUInt64),
                _ => throw new InvalidCastException($"Unable to cast from {value.Type} to uint: '{value}'"),
            };
        }

        /// <summary>
        /// Converts a <see cref="RedisValue"/> to a <see cref="long"/>.
        /// </summary>
        /// <param name="value">The <see cref="RedisValue"/> to convert.</param>
        [CLSCompliant(false)]
        public static explicit operator ulong(RedisValue value)
        {
            value = value.Simplify();
            return value.Type switch
            {
                StorageType.Null => 0, // in redis, an arithmetic zero is kinda the same thing as not-exists (think "incr")
                StorageType.Int64 => checked((ulong)value.OverlappedValueInt64), // throw if negative
                StorageType.UInt64 => value.OverlappedValueUInt64,
                _ => throw new InvalidCastException($"Unable to cast from {value.Type} to ulong: '{value}'"),
            };
        }

        /// <summary>
        /// Converts a <see cref="RedisValue"/> to a <see cref="double"/>.
        /// </summary>
        /// <param name="value">The <see cref="RedisValue"/> to convert.</param>
        public static explicit operator double(RedisValue value)
        {
            value = value.Simplify();
            return value.Type switch
            {
                StorageType.Null => 0, // in redis, an arithmetic zero is kinda the same thing as not-exists (think "incr")
                StorageType.Int64 => value.OverlappedValueInt64,
                StorageType.UInt64 => value.OverlappedValueUInt64,
                StorageType.Double => value.OverlappedValueDouble,
                // special values like NaN/Inf are deliberately not handled by Simplify, but need to be considered for casting
                StorageType.String when Format.TryParseDouble(value.RawString(), out var d) => d,
                StorageType.MemoryManager or StorageType.ByteArray or StorageType.ShortBlob when TryParseDouble(value.UnsafeRawSpan(out _), out var d) => d,
                StorageType.Sequence when value.TryParse(out double d) => d, // linearizes + handles inf/nan, like the span case above
                // anything else: fail
                _ => throw new InvalidCastException($"Unable to cast from {value.Type} to double: '{value}'"),
            };
        }

        /// <summary>
        /// Converts a <see cref="RedisValue"/> to a <see cref="decimal"/>.
        /// </summary>
        /// <param name="value">The <see cref="RedisValue"/> to convert.</param>
        public static explicit operator decimal(RedisValue value)
        {
            value = value.Simplify();
            return value.Type switch
            {
                StorageType.Null => 0, // in redis, an arithmetic zero is kinda the same thing as not-exists (think "incr")
                StorageType.Int64 => value.OverlappedValueInt64,
                StorageType.UInt64 => value.OverlappedValueUInt64,
                StorageType.Double => (decimal)value.OverlappedValueDouble,
                _ => throw new InvalidCastException($"Unable to cast from {value.Type} to decimal: '{value}'"),
            };
        }

        /// <summary>
        /// Converts a <see cref="RedisValue"/> to a <see cref="float"/>.
        /// </summary>
        /// <param name="value">The <see cref="RedisValue"/> to convert.</param>
        public static explicit operator float(RedisValue value)
        {
            value = value.Simplify();
            return value.Type switch
            {
                StorageType.Null => 0, // in redis, an arithmetic zero is kinda the same thing as not-exists (think "incr")
                StorageType.Int64 => value.OverlappedValueInt64,
                StorageType.UInt64 => value.OverlappedValueUInt64,
                StorageType.Double => (float)value.OverlappedValueDouble,
                _ => throw new InvalidCastException($"Unable to cast from {value.Type} to double: '{value}'"),
            };
        }

        private static bool TryParseDouble(ReadOnlySpan<byte> blob, out double value)
        {
            // simple integer?
            if (Format.CouldBeInteger(blob) && Format.TryParseInt64(blob, out var i64))
            {
                value = i64;
                return true;
            }

            return Format.TryParseDouble(blob, out value);
        }

        /// <summary>
        /// Converts the <see cref="RedisValue"/> to a <see cref="T:Nullable{double}"/>.
        /// </summary>
        /// <param name="value">The <see cref="RedisValue"/> to convert.</param>
        public static explicit operator double?(RedisValue value)
            => value.IsNull ? (double?)null : (double)value;

        /// <summary>
        /// Converts the <see cref="RedisValue"/> to a <see cref="T:Nullable{float}"/>.
        /// </summary>
        /// <param name="value">The <see cref="RedisValue"/> to convert.</param>
        public static explicit operator float?(RedisValue value)
            => value.IsNull ? (float?)null : (float)value;

        /// <summary>
        /// Converts the <see cref="RedisValue"/> to a <see cref="T:Nullable{decimal}"/>.
        /// </summary>
        /// <param name="value">The <see cref="RedisValue"/> to convert.</param>
        public static explicit operator decimal?(RedisValue value)
            => value.IsNull ? (decimal?)null : (decimal)value;

        /// <summary>
        /// Converts the <see cref="RedisValue"/> to a <see cref="T:Nullable{long}"/>.
        /// </summary>
        /// <param name="value">The <see cref="RedisValue"/> to convert.</param>
        public static explicit operator long?(RedisValue value)
            => value.IsNull ? (long?)null : (long)value;

        /// <summary>
        /// Converts the <see cref="RedisValue"/> to a <see cref="T:Nullable{ulong}"/>.
        /// </summary>
        /// <param name="value">The <see cref="RedisValue"/> to convert.</param>
        [CLSCompliant(false)]
        public static explicit operator ulong?(RedisValue value)
            => value.IsNull ? (ulong?)null : (ulong)value;

        /// <summary>
        /// Converts the <see cref="RedisValue"/> to a <see cref="T:Nullable{int}"/>.
        /// </summary>
        /// <param name="value">The <see cref="RedisValue"/> to convert.</param>
        public static explicit operator int?(RedisValue value)
            => value.IsNull ? (int?)null : (int)value;

        /// <summary>
        /// Converts the <see cref="RedisValue"/> to a <see cref="T:Nullable{uint}"/>.
        /// </summary>
        /// <param name="value">The <see cref="RedisValue"/> to convert.</param>
        [CLSCompliant(false)]
        public static explicit operator uint?(RedisValue value)
            => value.IsNull ? (uint?)null : (uint)value;

        /// <summary>
        /// Converts the <see cref="RedisValue"/> to a <see cref="T:Nullable{bool}"/>.
        /// </summary>
        /// <param name="value">The <see cref="RedisValue"/> to convert.</param>
        public static explicit operator bool?(RedisValue value)
            => value.IsNull ? (bool?)null : (bool)value;

        /// <summary>
        /// Converts a <see cref="RedisValue"/> to a <see cref="string"/>.
        /// </summary>
        /// <param name="value">The <see cref="RedisValue"/> to convert.</param>
        public static implicit operator string?(RedisValue value)
        {
            switch (value.Type)
            {
                case StorageType.Null: return null;
                case StorageType.Double: return Format.ToString(value.OverlappedValueDouble);
                case StorageType.Int64: return Format.ToString(value.OverlappedValueInt64);
                case StorageType.UInt64: return Format.ToString(value.OverlappedValueUInt64);
                case StorageType.String: return value.RawString();
                case StorageType.MemoryManager or StorageType.ByteArray or StorageType.ShortBlob:
                    var span = value.UnsafeRawSpan(out _);
                    if (span.IsEmpty) return "";
                    const ushort OkPackedLE = 'O' | ('K' << 8); // frequent special-case
                    if (span.Length is 2 && BinaryPrimitives.ReadUInt16LittleEndian(span) == OkPackedLE) return "OK";
                    try
                    {
                        return Format.GetString(span);
                    }
                    catch (Exception e) when // Only catch exception throwed by Encoding.UTF8.GetString
                        (e is DecoderFallbackException
                        || e is ArgumentException
                        || e is ArgumentNullException)
                    {
                        return ToHex(span);
                    }
                case StorageType.Sequence:
                    if (value._length == 0) return "";
                    var seq = value.RawSequence();
                    if (seq.IsEmpty) return "";
                    return Format.GetString(seq);
                default:
                    throw new InvalidOperationException();
            }
        }
        private static string ToHex(ReadOnlySpan<byte> src)
        {
            const string HexValues = "0123456789ABCDEF";

            if (src.IsEmpty) return "";
            var s = new string((char)0, (src.Length * 3) - 1);
            var dst = MemoryMarshal.AsMemory(s.AsMemory()).Span;

            int i = 0;
            int j = 0;

            byte b = src[i++];
            dst[j++] = HexValues[b >> 4];
            dst[j++] = HexValues[b & 0xF];

            while (i < src.Length)
            {
                b = src[i++];
                dst[j++] = '-';
                dst[j++] = HexValues[b >> 4];
                dst[j++] = HexValues[b & 0xF];
            }
            return s;
        }

        /// <summary>
        /// Converts a <see cref="RedisValue"/> to a <see cref="T:byte[]"/>.
        /// </summary>
        /// <param name="value">The <see cref="RedisValue"/> to convert.</param>
        public static implicit operator byte[]?(RedisValue value)
        {
            switch (value.Type)
            {
                case StorageType.Null: return null;
                case StorageType.ByteArray when value._obj is byte[] arr && value._index is 0 && value._length == arr.Length:
                    // the memory is backed by an array, and we're reading all of it
                    return arr;
                case StorageType.ByteArray or StorageType.MemoryManager or StorageType.ShortBlob:
                    return value.UnsafeRawSpan(out _).ToArray();
                case StorageType.Sequence:
                    return value.RawSequence().ToArray();
                case StorageType.Int64:
                    Debug.Assert(Format.MaxInt64TextLen <= 24);
                    Span<byte> span = stackalloc byte[24];
                    int len = Format.FormatInt64(value.OverlappedValueInt64, span);
                    return span.Slice(0, len).ToArray();
                case StorageType.UInt64:
                    Debug.Assert(Format.MaxInt64TextLen <= 24);
                    span = stackalloc byte[24];
                    len = Format.FormatUInt64(value.OverlappedValueUInt64, span);
                    return span.Slice(0, len).ToArray();
                case StorageType.Double:
                    span = stackalloc byte[Format.MaxDoubleTextLen];
                    len = Format.FormatDouble(value.OverlappedValueDouble, span);
                    return span.Slice(0, len).ToArray();
                case StorageType.String:
                    return Encoding.UTF8.GetBytes(value.RawString());
            }
            // fallback: stringify and encode
            return Encoding.UTF8.GetBytes((string)value!);
        }

        /// <summary>
        /// Gets the length of the value in bytes.
        /// </summary>
        public int GetByteCount() => Type switch
        {
            StorageType.Null => 0,
            StorageType.MemoryManager or StorageType.ByteArray or StorageType.Sequence or StorageType.ShortBlob => BlobLength,
            StorageType.String => Encoding.UTF8.GetByteCount(RawString()),
            StorageType.Int64 => Format.MeasureInt64(OverlappedValueInt64),
            StorageType.UInt64 => Format.MeasureUInt64(OverlappedValueUInt64),
            StorageType.Double => Format.MeasureDouble(OverlappedValueDouble),
            _ => ThrowUnableToMeasure(),
        };

        /// <summary>
        /// Gets the maximum length of the value in bytes.
        /// </summary>
        internal int GetMaxByteCount() => Type switch
        {
            StorageType.Null => 0,
            StorageType.MemoryManager or StorageType.ByteArray or StorageType.Sequence or StorageType.ShortBlob => BlobLength,
            StorageType.String => Encoding.UTF8.GetMaxByteCount(RawString().Length),
            StorageType.Int64 => Format.MaxInt64TextLen,
            StorageType.UInt64 => Format.MaxInt64TextLen,
            StorageType.Double => Format.MaxDoubleTextLen,
            _ => ThrowUnableToMeasure(),
        };

        /// <summary>
        /// Gets the length of the value in characters, assuming UTF8 interpretation of BLOB payloads.
        /// </summary>
        internal int GetCharCount() => Type switch
        {
            StorageType.Null => 0,
            StorageType.MemoryManager or StorageType.ByteArray or StorageType.ShortBlob => Encoding.UTF8.GetCharCount(UnsafeRawSpan(out _)),
            StorageType.Sequence => Encoding.UTF8.GetCharCount(RawSequence()),
            StorageType.String => _length,
            StorageType.Int64 => Format.MeasureInt64(OverlappedValueInt64),
            StorageType.UInt64 => Format.MeasureUInt64(OverlappedValueUInt64),
            StorageType.Double => Format.MeasureDouble(OverlappedValueDouble),
            _ => ThrowUnableToMeasure(),
        };

        /// <summary>
        /// Gets the length of the value in characters, assuming UTF8 interpretation of BLOB payloads.
        /// </summary>
        internal int GetMaxCharCount() => Type switch
        {
            StorageType.Null => 0,
            StorageType.MemoryManager or StorageType.ByteArray or StorageType.Sequence or StorageType.ShortBlob => Encoding.UTF8.GetMaxCharCount(BlobLength),
            StorageType.String => _length,
            StorageType.Int64 => Format.MaxInt64TextLen,
            StorageType.UInt64 => Format.MaxInt64TextLen,
            StorageType.Double => Format.MaxDoubleTextLen,
            _ => ThrowUnableToMeasure(),
        };

        private int ThrowUnableToMeasure() => throw new InvalidOperationException("Unable to compute length of type: " + Type);

        /// <summary>
        /// Gets the length of the value in bytes.
        /// </summary>
        /* right now, we only support int lengths, but adding this now so that
         there are no surprises if/when we add support for discontiguous buffers */
        public long GetLongByteCount() => GetByteCount();

        /// <summary>
        /// Copy the value as bytes to the provided <paramref name="destination"/>.
        /// </summary>
        public int CopyTo(Span<byte> destination)
        {
            switch (Type)
            {
                case StorageType.Null:
                    return 0;
                case StorageType.MemoryManager or StorageType.ByteArray or StorageType.ShortBlob:
                    var blob = UnsafeRawSpan(out _);
                    blob.CopyTo(destination);
                    return blob.Length;
                case StorageType.Sequence:
                    RawSequence().CopyTo(destination);
                    return _length;
                case StorageType.String:
                    return Encoding.UTF8.GetBytes(RawString().AsSpan(), destination);
                case StorageType.Int64:
                    return Format.FormatInt64(OverlappedValueInt64, destination);
                case StorageType.UInt64:
                    return Format.FormatUInt64(OverlappedValueUInt64, destination);
                case StorageType.Double:
                    return Format.FormatDouble(OverlappedValueDouble, destination);
                default:
                    return ThrowUnableToMeasure();
            }
        }

        /// <summary>
        /// Copy the value as character data to the provided <paramref name="destination"/>.
        /// </summary>
        internal int CopyTo(Span<char> destination)
        {
            switch (Type)
            {
                case StorageType.Null:
                    return 0;
                case StorageType.MemoryManager or StorageType.ByteArray or StorageType.ShortBlob:
                    return Encoding.UTF8.GetChars(UnsafeRawSpan(out _), destination);
                case StorageType.Sequence:
                    return Encoding.UTF8.GetChars(RawSequence(), destination);
                case StorageType.String:
                    var span = RawString().AsSpan();
                    span.CopyTo(destination);
                    return span.Length;
                case StorageType.Int64:
                    return Format.FormatInt64(OverlappedValueInt64, destination);
                case StorageType.UInt64:
                    return Format.FormatUInt64(OverlappedValueUInt64, destination);
                case StorageType.Double:
                    return Format.FormatDouble(OverlappedValueDouble, destination);
                default:
                    return ThrowUnableToMeasure();
            }
        }

        /// <summary>
        /// Converts a <see cref="RedisValue"/> to a <see cref="ReadOnlyMemory{T}"/>.
        /// </summary>
        /// <param name="value">The <see cref="RedisValue"/> to convert.</param>
        public static implicit operator ReadOnlyMemory<byte>(RedisValue value)
        {
            if (value._obj is byte[] arr) return new ReadOnlyMemory<byte>(arr, value._index, value._length);
            if (value._obj is MemoryManager<byte> manager) return manager.Memory.Slice(value._index, value._length);
            if (value._obj is null) return default;
            return (byte[]?)value;
        }

        /// <summary>
        /// Converts a <see cref="RedisValue"/> to a <see cref="ReadOnlySequence{T}"/>.
        /// </summary>
        /// <param name="value">The <see cref="RedisValue"/> to convert.</param>
        public static implicit operator ReadOnlySequence<byte>(RedisValue value)
        {
            if (value._obj is ReadOnlySequenceSegment<byte> s) return GetSequence(s, value._index, value._length);
            return new((ReadOnlyMemory<byte>)value);
        }

        TypeCode IConvertible.GetTypeCode() => TypeCode.Object;

        bool IConvertible.ToBoolean(IFormatProvider? provider) => (bool)this;
        byte IConvertible.ToByte(IFormatProvider? provider) => (byte)(uint)this;
        char IConvertible.ToChar(IFormatProvider? provider) => (char)(uint)this;
        DateTime IConvertible.ToDateTime(IFormatProvider? provider) => DateTime.Parse(((string?)this)!, provider);
        decimal IConvertible.ToDecimal(IFormatProvider? provider) => (decimal)this;
        double IConvertible.ToDouble(IFormatProvider? provider) => (double)this;
        short IConvertible.ToInt16(IFormatProvider? provider) => (short)this;
        int IConvertible.ToInt32(IFormatProvider? provider) => (int)this;
        long IConvertible.ToInt64(IFormatProvider? provider) => (long)this;
        sbyte IConvertible.ToSByte(IFormatProvider? provider) => (sbyte)this;
        float IConvertible.ToSingle(IFormatProvider? provider) => (float)this;
        string IConvertible.ToString(IFormatProvider? provider) => ((string?)this)!;

        object IConvertible.ToType(Type conversionType, IFormatProvider? provider)
        {
            if (conversionType == null) throw new ArgumentNullException(nameof(conversionType));
            if (conversionType == typeof(byte[])) return ((byte[]?)this)!;
            if (conversionType == typeof(ReadOnlyMemory<byte>)) return (ReadOnlyMemory<byte>)this;
            if (conversionType == typeof(RedisValue)) return this;
            return System.Type.GetTypeCode(conversionType) switch
            {
                TypeCode.Boolean => (bool)this,
                TypeCode.Byte => checked((byte)(uint)this),
                TypeCode.Char => checked((char)(uint)this),
                TypeCode.DateTime => DateTime.Parse(((string?)this)!, provider),
                TypeCode.Decimal => (decimal)this,
                TypeCode.Double => (double)this,
                TypeCode.Int16 => (short)this,
                TypeCode.Int32 => (int)this,
                TypeCode.Int64 => (long)this,
                TypeCode.SByte => (sbyte)this,
                TypeCode.Single => (float)this,
                TypeCode.String => ((string?)this)!,
                TypeCode.UInt16 => checked((ushort)(uint)this),
                TypeCode.UInt32 => (uint)this,
                TypeCode.UInt64 => (ulong)this,
                TypeCode.Object => this,
                _ => throw new NotSupportedException(),
            };
        }

        ushort IConvertible.ToUInt16(IFormatProvider? provider) => checked((ushort)(uint)this);
        uint IConvertible.ToUInt32(IFormatProvider? provider) => (uint)this;
        ulong IConvertible.ToUInt64(IFormatProvider? provider) => (ulong)this;

        /// <summary>
        /// Attempt to reduce to canonical terms ahead of time; parses integers, floats, etc
        /// Note: we don't use this aggressively ahead of time, a: because of extra CPU,
        /// but more importantly b: because it can change values - for example, if they start
        /// with "123.000", it should **stay** as "123.000", not become 123L; this could be
        /// a hash key or similar - we don't want to break it; RedisConnection uses
        /// the storage type, not the "does it look like a long?" - for this reason.
        /// </summary>
        internal RedisValue Simplify()
        {
            long i64;
            ulong u64;
            switch (Type)
            {
                case StorageType.String:
                    string s = RawString();
                    if (Format.CouldBeInteger(s))
                    {
                        if (Format.TryParseInt64(s, out i64)) return i64;
                        if (Format.TryParseUInt64(s, out u64)) return u64;
                    }
                    // note: don't simplify inf/nan, as that causes equality semantic problems
                    if (Format.TryParseDouble(s, out var f64) && !IsSpecialDouble(f64)) return f64;
                    break;
                case StorageType.MemoryManager or StorageType.ByteArray or StorageType.ShortBlob:
                    if (TrySimplify(UnsafeRawSpan(out _), out var simplified)) return simplified;
                    break;
                case StorageType.Sequence:
                    // numeric forms are short, so we only need to consider plausibly-numeric lengths;
                    // copy into a small stack buffer so we can reuse the exact same byte-based parsing
                    var seq = RawSequence();
                    if (seq.Length <= Format.MaxDoubleTextLen)
                    {
                        Span<byte> tmp = stackalloc byte[Format.MaxDoubleTextLen];
                        int len = (int)seq.Length;
                        seq.CopyTo(tmp);
                        if (TrySimplify(tmp.Slice(0, len), out simplified)) return simplified;
                    }
                    break;
                case StorageType.Double:
                    // is the double actually an integer?
                    f64 = OverlappedValueDouble;
                    if (f64 >= long.MinValue && f64 <= long.MaxValue && (i64 = (long)f64) == f64) return i64;
                    break;
            }
            return this;

            // shared by the ByteArray/MemoryManager and Sequence cases, so that identical bytes
            // simplify identically regardless of how they happen to be stored
            static bool TrySimplify(ReadOnlySpan<byte> bytes, out RedisValue value)
            {
                if (Format.CouldBeInteger(bytes))
                {
                    if (Format.TryParseInt64(bytes, out var i64))
                    {
                        value = i64;
                        return true;
                    }
                    if (Format.TryParseUInt64(bytes, out var u64))
                    {
                        value = u64;
                        return true;
                    }
                }
                // note: don't simplify inf/nan, as that causes equality semantic problems
                if (TryParseDouble(bytes, out var f64) && !IsSpecialDouble(f64))
                {
                    value = f64;
                    return true;
                }
                value = default;
                return false;
            }
        }

        private static bool IsSpecialDouble(double d) => double.IsNaN(d) || double.IsInfinity(d);

        /// <summary>
        /// Convert to a signed <see cref="long"/> if possible.
        /// </summary>
        /// <param name="val">The <see cref="long"/> value, if conversion was possible.</param>
        /// <returns><see langword="true"/> if successfully parsed, <see langword="false"/> otherwise.</returns>
        public bool TryParse(out long val)
        {
            switch (Type)
            {
                case StorageType.Int64:
                    val = OverlappedValueInt64;
                    return true;
                case StorageType.UInt64:
                    // we only use unsigned for oversize, so no: it doesn't fit
                    val = default;
                    return false;
                case StorageType.String:
                    return Format.TryParseInt64(RawString(), out val);
                case StorageType.MemoryManager or StorageType.ByteArray or StorageType.ShortBlob:
                    return Format.TryParseInt64(UnsafeRawSpan(out _), out val);
                case StorageType.Sequence:
                    // longer than the largest possible Int64 text => cannot be an Int64; otherwise
                    // linearize onto the stack and reuse the span-based parse (matching the ByteArray path)
                    if (_length <= Format.MaxInt64TextLen)
                    {
                        Span<byte> buffer = stackalloc byte[Format.MaxInt64TextLen];
                        return Format.TryParseInt64(CopyRawSequence(buffer), out val);
                    }
                    break;
                case StorageType.Double:
                    var d = OverlappedValueDouble;
                    try
                    {
                        val = (long)d;
                    }
                    catch
                    {
                        val = default;
                        return false;
                    }
                    return val == d;
                case StorageType.Null:
                    // in redis-land 0 approx. equal null; so roll with it
                    val = 0;
                    return true;
            }
            val = default;
            return false;
        }

        /// <summary>
        /// Convert to an <see cref="int"/> if possible.
        /// </summary>
        /// <param name="val">The <see cref="int"/> value, if conversion was possible.</param>
        /// <returns><see langword="true"/> if successfully parsed, <see langword="false"/> otherwise.</returns>
        public bool TryParse(out int val)
        {
            if (!TryParse(out long l) || l > int.MaxValue || l < int.MinValue)
            {
                val = 0;
                return false;
            }

            val = (int)l;
            return true;
        }

        /// <summary>
        /// Convert to a <see cref="double"/> if possible.
        /// </summary>
        /// <param name="val">The <see cref="double"/> value, if conversion was possible.</param>
        /// <returns><see langword="true"/> if successfully parsed, <see langword="false"/> otherwise.</returns>
        public bool TryParse(out double val)
        {
            switch (Type)
            {
                case StorageType.Int64:
                    val = OverlappedValueInt64;
                    return true;
                case StorageType.UInt64:
                    val = OverlappedValueUInt64;
                    return true;
                case StorageType.Double:
                    val = OverlappedValueDouble;
                    return true;
                case StorageType.String:
                    return Format.TryParseDouble(RawString(), out val);
                case StorageType.MemoryManager or StorageType.ByteArray or StorageType.ShortBlob:
                    return TryParseDouble(UnsafeRawSpan(out _), out val);
                case StorageType.Sequence:
                    // longer than the largest possible double text => cannot be a double; otherwise
                    // linearize onto the stack and reuse the span-based parse (matching the ByteArray path)
                    if (_length <= Format.MaxDoubleTextLen)
                    {
                        Span<byte> buffer = stackalloc byte[Format.MaxDoubleTextLen];
                        return TryParseDouble(CopyRawSequence(buffer), out val);
                    }
                    break;
                case StorageType.Null:
                    // in redis-land 0 approx. equal null; so roll with it
                    val = 0;
                    return true;
            }
            val = default;
            return false;
        }

        /// <summary>
        /// Create a <see cref="RedisValue"/> from a <see cref="MemoryStream"/>.
        /// It will *attempt* to use the internal buffer directly, but if this isn't possible it will fallback to <see cref="MemoryStream.ToArray"/>.
        /// </summary>
        /// <param name="stream">The <see cref="MemoryStream"/> to create a value from.</param>
        public static RedisValue CreateFrom(MemoryStream stream)
        {
            if (stream == null) return Null;
            if (stream.Length == 0) return Array.Empty<byte>();
            if (stream.TryGetBuffer(out var segment) || ReflectionTryGetBuffer(stream, out segment))
            {
                return new Memory<byte>(segment.Array, segment.Offset, segment.Count);
            }
            else
            {
                // nowhere near as efficient, but...
                return stream.ToArray();
            }
        }

        private static readonly FieldInfo?
            s_origin = typeof(MemoryStream).GetField("_origin", BindingFlags.NonPublic | BindingFlags.Instance),
            s_buffer = typeof(MemoryStream).GetField("_buffer", BindingFlags.NonPublic | BindingFlags.Instance);

        private static bool ReflectionTryGetBuffer(MemoryStream ms, out ArraySegment<byte> buffer)
        {
            if (s_origin != null && s_buffer != null)
            {
                try
                {
                    int offset = (int)s_origin.GetValue(ms)!;
                    byte[] arr = (byte[])s_buffer.GetValue(ms)!;
                    buffer = new ArraySegment<byte>(arr, offset, checked((int)ms.Length));
                    return true;
                }
                catch { }
            }
            buffer = default;
            return false;
        }

        /// <summary>
        /// Indicates whether the current value has the supplied value as a prefix.
        /// </summary>
        /// <param name="value">The <see cref="RedisValue"/> to check.</param>
        public bool StartsWith(RedisValue value)
        {
            if (IsNull || value.IsNull) return false;
            if (value.IsNullOrEmpty) return true;
            if (IsNullOrEmpty) return false;

            var thisType = Type;
            var otherType = value.Type;
            if (thisType == otherType) // same? can often optimize
            {
                switch (thisType)
                {
                    case StorageType.String:
                        var sThis = RawString();
                        var sOther = value.RawString();
                        return sThis.StartsWith(sOther, StringComparison.Ordinal);
                    case StorageType.MemoryManager or StorageType.ByteArray or StorageType.ShortBlob or StorageType.Sequence:
                        return BlobStartsWith(this, value);
                }
            }

            // mixed byte-backed kinds (byte[] / memory / short-blob / sequence) compare by raw bytes
            if (IsBlob(thisType) && IsBlob(otherType))
            {
                return BlobStartsWith(this, value);
            }
            byte[]? arr0 = null, arr1 = null;
            try
            {
                var rawThis = AsMemory(out arr0);
                var rawOther = value.AsMemory(out arr1);

                return rawThis.Span.StartsWith(rawOther.Span);
            }
            finally
            {
                if (arr0 != null) ArrayPool<byte>.Shared.Return(arr0);
                if (arr1 != null) ArrayPool<byte>.Shared.Return(arr1);
            }
        }

        private ReadOnlyMemory<byte> AsMemory(out byte[]? leased)
        {
            switch (Type)
            {
                case StorageType.MemoryManager:
                    leased = null;
                    return ((MemoryManager<byte>)_obj!).Memory.Slice(_index, _length);
                case StorageType.ByteArray:
                    leased = null;
                    return new ReadOnlyMemory<byte>((byte[])_obj!, _index, _length);
                case StorageType.Sequence:
                    leased = ArrayPool<byte>.Shared.Rent(_length);
                    RawSequence().CopyTo(leased);
                    return new ReadOnlyMemory<byte>(leased, 0, _length);
                case StorageType.ShortBlob:
                    var blob = UnsafeRawSpan(out _);
                    leased = ArrayPool<byte>.Shared.Rent(blob.Length);
                    blob.CopyTo(leased);
                    return new ReadOnlyMemory<byte>(leased, 0, blob.Length);
                case StorageType.String:
                    string s = RawString();
HaveString:
                    if (s.Length == 0)
                    {
                        leased = null;
                        return default;
                    }
                    leased = ArrayPool<byte>.Shared.Rent(Encoding.UTF8.GetByteCount(s));
                    var len = Encoding.UTF8.GetBytes(s, 0, s.Length, leased, 0);
                    return new ReadOnlyMemory<byte>(leased, 0, len);
                case StorageType.Double:
                    s = Format.ToString(OverlappedValueDouble);
                    goto HaveString;
                case StorageType.Int64:
                    leased = ArrayPool<byte>.Shared.Rent(Format.MaxInt64TextLen + 2); // reused code has CRLF terminator
                    len = MessageWriter.WriteRaw(leased, OverlappedValueInt64) - 2; // drop the CRLF
                    return new ReadOnlyMemory<byte>(leased, 0, len);
                case StorageType.UInt64:
                    leased = ArrayPool<byte>.Shared.Rent(Format.MaxInt64TextLen); // reused code has CRLF terminator
                    // value is huge, jump direct to Utf8Formatter
                    if (!Utf8Formatter.TryFormat(OverlappedValueUInt64, leased, out len))
                        throw new InvalidOperationException("TryFormat failed");
                    return new ReadOnlyMemory<byte>(leased, 0, len);
            }
            leased = null;
            return default;
        }

        /// <summary>
        /// Get the digest (hash used for check-and-set/check-and-delete operations) of this value.
        /// </summary>
        internal ValueCondition Digest()
        {
            switch (Type)
            {
                case StorageType.MemoryManager or StorageType.ByteArray or StorageType.ShortBlob:
                    return ValueCondition.CalculateDigest(UnsafeRawSpan(out _));
                case StorageType.Sequence:
                    return ValueCondition.CalculateDigest(RawSequence());
                case StorageType.Null:
                    return ValueCondition.NotExists; // interpret === null as "not exists"
                default:
                    var len = GetByteCount();
                    byte[]? oversized = null;
                    Span<byte> buffer = len <= 128 ? stackalloc byte[128] : (oversized = ArrayPool<byte>.Shared.Rent(len));
                    CopyTo(buffer);
                    var digest = ValueCondition.CalculateDigest(buffer.Slice(0, len));
                    if (oversized is not null) ArrayPool<byte>.Shared.Return(oversized);
                    return digest;
            }
        }

        internal bool TryGetSpan(out ReadOnlySpan<byte> span)
        {
            if (_obj is MemoryManager<byte> manager)
            {
                span = manager.Memory.Span.Slice(_index, _length);
                return true;
            }
            if (_obj is byte[] bytes)
            {
                span = new ReadOnlySpan<byte>(bytes, _index, _length);
                return true;
            }
            span = default;
            return false;
        }

        /// <summary>
        /// Indicates whether the current value has the supplied value as a prefix.
        /// </summary>
        /// <param name="value">The <see cref="RedisValue"/> to check.</param>
        [OverloadResolutionPriority(1)] // prefer this when it is an option (vs casting a byte[] to RedisValue)
        public bool StartsWith(ReadOnlySpan<byte> value)
        {
            if (IsNull) return false;
            if (value.IsEmpty) return true;
            if (IsNullOrEmpty) return false;

            int len;
            switch (Type)
            {
                case StorageType.MemoryManager or StorageType.ByteArray or StorageType.ShortBlob:
                    return UnsafeRawSpan(out _).StartsWith(value);
                case StorageType.Sequence:
                    return RawSequence().StartsWith(value);
                case StorageType.Int64:
                    Span<byte> buffer = stackalloc byte[Format.MaxInt64TextLen];
                    len = Format.FormatInt64(OverlappedValueInt64, buffer);
                    return buffer.Slice(0, len).StartsWith(value);
                case StorageType.UInt64:
                    buffer = stackalloc byte[Format.MaxInt64TextLen];
                    len = Format.FormatUInt64(OverlappedValueUInt64, buffer);
                    return buffer.Slice(0, len).StartsWith(value);
                case StorageType.Double:
                    buffer = stackalloc byte[Format.MaxDoubleTextLen];
                    len = Format.FormatDouble(OverlappedValueDouble, buffer);
                    return buffer.Slice(0, len).StartsWith(value);
                case StorageType.String:
                    var s = RawString().AsSpan();
                    if (s.Length < value.Length) return false; // not enough characters to match
                    if (s.Length > value.Length) s = s.Slice(0, value.Length); // only need to match the prefix
                    var maxBytes = Encoding.UTF8.GetMaxByteCount(s.Length);
                    byte[]? lease = null;
                    const int MAX_STACK = 128;
                    buffer = maxBytes <= MAX_STACK ? stackalloc byte[MAX_STACK] : (lease = ArrayPool<byte>.Shared.Rent(maxBytes));
                    var bytes = Encoding.UTF8.GetBytes(s, buffer);
                    bool isMatch = buffer.Slice(0, bytes).StartsWith(value);
                    if (lease is not null) ArrayPool<byte>.Shared.Return(lease);
                    return isMatch;
                default:
                    return false;
            }
        }
    }
}
