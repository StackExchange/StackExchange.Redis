using System;
using System.Buffers;
using System.Text;

namespace StackExchange.Redis
{
    public readonly struct CommandBytes : IEquatable<CommandBytes>
    {
        private static Encoding Encoding => Encoding.UTF8;

        // Uses [n=4] x UInt64 values to store a command payload,
        // allowing allocation free storage and efficient
        // equality tests. If you're glancing at this and thinking
        // "that's what fixed buffers are for", please see:
        // https://github.com/dotnet/coreclr/issues/19149
        //
        // note: this tries to use case insensitive comparison
        private readonly ulong _0, _1, _2;
        private const int ChunkLength = 3; // must reflect qty above

        public const int MaxLength = (ChunkLength * sizeof(ulong)) - 1;

        public override int GetHashCode()
        {
            var hashCode = -1923861349;
            hashCode = hashCode * -1521134295 + _0.GetHashCode();
            hashCode = hashCode * -1521134295 + _1.GetHashCode();
            hashCode = hashCode * -1521134295 + _2.GetHashCode();
            return hashCode;
        }
        public override bool Equals(object obj) => obj is CommandBytes cb && Equals(cb);

        public bool Equals(CommandBytes value) => _0 == value._0 && _1 == value._1 && _2 == value._2;

        public static bool operator == (CommandBytes x, CommandBytes y) => x.Equals(y);
        public static bool operator !=(CommandBytes x, CommandBytes y) => !x.Equals(y);

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

        public unsafe CommandBytes(string value)
        {
            var len = Encoding.GetByteCount(value);
            if (len > MaxLength) throw new ArgumentOutOfRangeException("Maximum command length exceeed");
            _0 = _1 = _2 = 0L;
            fixed (ulong* uPtr = &_0)
            {
                byte* bPtr = (byte*)uPtr;
                fixed (char* cPtr = value)
                {
                    len = Encoding.GetBytes(cPtr, value.Length, bPtr + 1, MaxLength);
                }
                *bPtr = (byte)LowerCasify(len, bPtr + 1);
            }
        }

        public unsafe CommandBytes(ReadOnlySpan<byte> value)
        {
            if (value.Length > MaxLength) throw new ArgumentOutOfRangeException("Maximum command length exceeed");
            _0 = _1 = _2 = 0L;
            fixed (ulong* uPtr = &_0)
            {
                byte* bPtr = (byte*)uPtr;
                value.CopyTo(new Span<byte>(bPtr + 1, value.Length));
                *bPtr = (byte)LowerCasify(value.Length, bPtr + 1);
            }
        }
        public unsafe CommandBytes(ReadOnlySequence<byte> value)
        {
            if (value.Length > MaxLength) throw new ArgumentOutOfRangeException("Maximum command length exceeed");
            int len = unchecked((int)value.Length);
            _0 = _1 = _2 = 0L;
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
                *bPtr = (byte)LowerCasify(len, bPtr + 1);
            }
        }
        private unsafe int LowerCasify(int len, byte* bPtr)
        {
            const ulong HighBits = 0x8080808080808080;
            if (((_0 | _1 | _2) & HighBits) == 0)
            {
                // no unicode; use ASCII bit bricks
                for (int i = 0; i < len; i++)
                {
                    *bPtr = ToLowerInvariantAscii(*bPtr++);
                }
                return len;
            }
            else
            {
                return LowerCasifyUnicode(len, bPtr);
            }
        }

        private static unsafe int LowerCasifyUnicode(int oldLen, byte* bPtr)
        {
            const int MaxChars = ChunkLength * sizeof(ulong); // leave rounded up; helps stackalloc
            char* workspace = stackalloc char[MaxChars];
            int charCount = Encoding.GetChars(bPtr, oldLen, workspace, MaxChars);
            char* c = workspace;
            for (int i = 0; i < charCount; i++) *c = char.ToLowerInvariant((*c++));
            int newLen = Encoding.GetBytes(workspace, charCount, bPtr, MaxLength);
            // don't forget to zero any shrink
            for (int i = newLen; i < oldLen; i++) bPtr[i] = 0;
            return newLen;
        }

        static byte ToLowerInvariantAscii(byte b) => b >= 'A' && b <= 'Z' ? (byte)(b | 32) : b;

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
