using System;
using System.Buffers;
using System.Text;

namespace StackExchange.Redis
{
    internal readonly struct CommandBytes : IEquatable<CommandBytes>
    {
        private static Encoding Encoding => Encoding.UTF8;

        internal static unsafe CommandBytes TrimToFit(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return default;
            value = value.Trim();
            var len = Encoding.GetByteCount(value);
            if (len <= MaxLength) return new CommandBytes(value); // all fits

            fixed (char* c = value)
            {
                byte* b = stackalloc byte[ChunkLength * sizeof(ulong)];
                var encoder = PhysicalConnection.GetPerThreadEncoder();
                encoder.Convert(c, value.Length, b, MaxLength, true, out var maxLen, out _, out var isComplete);
                if (!isComplete) maxLen--;
                return new CommandBytes(value.Substring(0, maxLen));
            }
        }

        // Uses [n=4] x UInt64 values to store a command payload,
        // allowing allocation free storage and efficient
        // equality tests. If you're glancing at this and thinking
        // "that's what fixed buffers are for", please see:
        // https://github.com/dotnet/coreclr/issues/19149
        //
        // note: this tries to use case insensitive comparison
        private readonly ulong _0, _1, _2, _3;
        private const int ChunkLength = 4; // must reflect qty above

        public const int MaxLength = (ChunkLength * sizeof(ulong)) - 1;

        public override int GetHashCode()
        {
            var hashCode = -1923861349;
            hashCode = (hashCode * -1521134295) + _0.GetHashCode();
            hashCode = (hashCode * -1521134295) + _1.GetHashCode();
            hashCode = (hashCode * -1521134295) + _2.GetHashCode();
            hashCode = (hashCode * -1521134295) + _3.GetHashCode();
            return hashCode;
        }

        public override bool Equals(object? obj) => obj is CommandBytes cb && Equals(cb);

        bool IEquatable<CommandBytes>.Equals(CommandBytes other) => _0 == other._0 && _1 == other._1 && _2 == other._2 && _3 == other._3;

        public bool Equals(in CommandBytes other) => _0 == other._0 && _1 == other._1 && _2 == other._2 && _3 == other._3;

        // note: don't add == operators; with the implicit op above, that invalidates "==null" compiler checks (which should report a failure!)

        public static implicit operator CommandBytes(string value) => new CommandBytes(value);

        public override unsafe string ToString()
        {
            fixed (ulong* uPtr = &_0)
            {
                var bPtr = (byte*)uPtr;
                int len = *bPtr;
                return len == 0 ? "" : Encoding.GetString(bPtr + 1, *bPtr);
            }
        }
        public unsafe int Length
        {
            get
            {
                fixed (ulong* uPtr = &_0)
                {
                    return *(byte*)uPtr;
                }
            }
        }

        public bool IsEmpty => _0 == 0L; // cheap way of checking zero length

        public unsafe void CopyTo(Span<byte> target)
        {
            fixed (ulong* uPtr = &_0)
            {
                byte* bPtr = (byte*)uPtr;
                new Span<byte>(bPtr + 1, *bPtr).CopyTo(target);
            }
        }

        public unsafe byte this[int index]
        {
            get
            {
                fixed (ulong* uPtr = &_0)
                {
                    byte* bPtr = (byte*)uPtr;
                    int len = *bPtr;
                    if (index < 0 || index >= len) throw new IndexOutOfRangeException();
                    return bPtr[index + 1];
                }
            }
        }

        public unsafe CommandBytes(string? value)
        {
            _0 = _1 = _2 = _3 = 0L;
            if (value.IsNullOrEmpty()) return;

            var len = Encoding.GetByteCount(value);
            if (len > MaxLength) throw new ArgumentOutOfRangeException($"Command '{value}' exceeds library limit of {MaxLength} bytes");
            fixed (ulong* uPtr = &_0)
            {
                byte* bPtr = (byte*)uPtr;
                fixed (char* cPtr = value)
                {
                    len = Encoding.GetBytes(cPtr, value.Length, bPtr + 1, MaxLength);
                }
                *bPtr = (byte)UpperCasify(len, bPtr + 1);
            }
        }

        public unsafe CommandBytes(ReadOnlySpan<byte> value)
        {
            if (value.Length > MaxLength) throw new ArgumentOutOfRangeException("Maximum command length exceeded: " + value.Length + " bytes");
            _0 = _1 = _2 = _3 = 0L;
            fixed (ulong* uPtr = &_0)
            {
                byte* bPtr = (byte*)uPtr;
                value.CopyTo(new Span<byte>(bPtr + 1, value.Length));
                *bPtr = (byte)UpperCasify(value.Length, bPtr + 1);
            }
        }

        public unsafe CommandBytes(in ReadOnlySequence<byte> value)
        {
            if (value.Length > MaxLength) throw new ArgumentOutOfRangeException(nameof(value), "Maximum command length exceeded");
            int len = unchecked((int)value.Length);
            _0 = _1 = _2 = _3 = 0L;
            fixed (ulong* uPtr = &_0)
            {
                byte* bPtr = (byte*)uPtr;
                var target = new Span<byte>(bPtr + 1, len);

                if (value.IsSingleSegment)
                {
                    value.First.Span.CopyTo(target);
                }
                else
                {
                    foreach (var segment in value)
                    {
                        segment.Span.CopyTo(target);
                        target = target.Slice(segment.Length);
                    }
                }
                *bPtr = (byte)UpperCasify(len, bPtr + 1);
            }
        }
        private unsafe int UpperCasify(int len, byte* bPtr)
        {
            const ulong HighBits = 0x8080808080808080;
            if (((_0 | _1 | _2 | _3) & HighBits) == 0)
            {
                // no Unicode; use ASCII bit bricks
                for (int i = 0; i < len; i++)
                {
                    *bPtr = ToUpperInvariantAscii(*bPtr++);
                }
                return len;
            }
            else
            {
                return UpperCasifyUnicode(len, bPtr);
            }
        }

        private static unsafe int UpperCasifyUnicode(int oldLen, byte* bPtr)
        {
            const int MaxChars = ChunkLength * sizeof(ulong); // leave rounded up; helps stackalloc
            char* workspace = stackalloc char[MaxChars];
            int charCount = Encoding.GetChars(bPtr, oldLen, workspace, MaxChars);
            char* c = workspace;
            for (int i = 0; i < charCount; i++) *c = char.ToUpperInvariant(*c++);
            int newLen = Encoding.GetBytes(workspace, charCount, bPtr, MaxLength);
            // don't forget to zero any shrink
            for (int i = newLen; i < oldLen; i++) bPtr[i] = 0;
            return newLen;
        }

        private static byte ToUpperInvariantAscii(byte b) => b >= 'a' && b <= 'z' ? (byte)(b - 32) : b;

        internal unsafe byte[] ToArray()
        {
            fixed (ulong* uPtr = &_0)
            {
                byte* bPtr = (byte*)uPtr;
                return new Span<byte>(bPtr + 1, *bPtr).ToArray();
            }
        }
    }
}
