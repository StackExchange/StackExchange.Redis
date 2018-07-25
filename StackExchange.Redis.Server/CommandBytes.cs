using System;
using System.Text;

namespace StackExchange.Redis
{
    public unsafe struct CommandBytes : IEquatable<CommandBytes>
    {
        public override int GetHashCode() => _hashcode;
        public override string ToString()
        {
            fixed (byte* ptr = _bytes)
            {
                return Encoding.UTF8.GetString(ptr, Length);
            }
        }
        public byte this[int index]
        {
            get
            {
                if (index < 0 || index >= Length) throw new IndexOutOfRangeException();
                fixed (byte* ptr = _bytes)
                {
                    return ptr[index];
                }
            }
        }
        public const int MaxLength = 32; // mut be multiple of 8
        public int Length { get; }
        readonly int _hashcode;
        fixed byte _bytes[MaxLength];
        public CommandBytes(string value)
        {
            value = value.ToLowerInvariant();
            Length = Encoding.UTF8.GetByteCount(value);
            if (Length > MaxLength) throw new ArgumentOutOfRangeException("Maximum command length exceeed");
            fixed (byte* bPtr = _bytes)
            {
                Clear((long*)bPtr);
                fixed (char* cPtr = value)
                {
                    Encoding.UTF8.GetBytes(cPtr, value.Length, bPtr, Length);
                }
                _hashcode = GetHashCode(bPtr, Length);
            }
        }
        public override bool Equals(object obj) => obj is CommandBytes cb && Equals(cb);
        public bool Equals(CommandBytes value)
        {
            if (_hashcode != value._hashcode || Length != value.Length)
                return false;
            fixed (byte* thisB = _bytes)
            {
                var thisL = (long*)thisB;
                var otherL = (long*)value._bytes;
                int chunks = (Length + 7) >> 3;
                for (int i = 0; i < chunks; i++)
                {
                    if (thisL[i] != otherL[i]) return false;
                }
            }
            return true;
        }
        private static void Clear(long* ptr)
        {   
            for (int i = 0; i < (MaxLength >> 3) ; i++)
            {
                ptr[i] = 0;
            }
        }

        public CommandBytes(ReadOnlySpan<byte> value)
        {
            Length = value.Length;
            if (Length > MaxLength) throw new ArgumentOutOfRangeException("Maximum command length exceeed");
            fixed (byte* bPtr = _bytes)
            {
                Clear((long*)bPtr);
                for (int i = 0; i < value.Length; i++)
                {
                    bPtr[i] = ToLowerInvariant(value[i]);
                }
                _hashcode = GetHashCode(bPtr, Length);
            }
        }
        static int GetHashCode(byte* ptr, int count)
        {
            var hc = count;
            for (int i = 0; i < count; i++)
            {
                hc = (hc * -13547) + ptr[i];
            }
            return hc;
        }
        static byte ToLowerInvariant(byte b) => b >= 'A' && b <= 'Z' ? (byte)(b | 32) : b;

        internal byte[] ToArray()
        {
            fixed (byte* ptr = _bytes)
            {
                return new Span<byte>(ptr, Length).ToArray();
            }
        }
    }
}
