using System.Runtime.InteropServices;

#if !NET
// ReSharper disable once CheckNamespace
namespace System.Text
{
    internal static class EncodingExtensions
    {
        public static unsafe int GetBytes(this Encoding encoding, ReadOnlySpan<char> source, Span<byte> destination)
        {
            if (source.IsEmpty) return 0;
            fixed (byte* bPtr = &MemoryMarshal.GetReference(destination))
            {
                fixed (char* cPtr = source)
                {
                    return encoding.GetBytes(cPtr, source.Length, bPtr, destination.Length);
                }
            }
        }

        public static unsafe int GetChars(this Encoding encoding, ReadOnlySpan<byte> source, Span<char> destination)
        {
            if (source.IsEmpty) return 0;
            fixed (byte* bPtr = &MemoryMarshal.GetReference(source))
            {
                fixed (char* cPtr = &MemoryMarshal.GetReference(destination))
                {
                    return encoding.GetChars(bPtr, source.Length, cPtr, destination.Length);
                }
            }
        }

        public static unsafe int GetCharCount(this Encoding encoding, ReadOnlySpan<byte> source)
        {
            if (source.IsEmpty) return 0;
            fixed (byte* bPtr = &MemoryMarshal.GetReference(source))
            {
                return encoding.GetCharCount(bPtr, source.Length);
            }
        }

        public static unsafe string GetString(this Encoding encoding, ReadOnlySpan<byte> source)
        {
            if (source.IsEmpty) return "";
            fixed (byte* bPtr = &MemoryMarshal.GetReference(source))
            {
                return encoding.GetString(bPtr, source.Length);
            }
        }
    }
}
#endif
