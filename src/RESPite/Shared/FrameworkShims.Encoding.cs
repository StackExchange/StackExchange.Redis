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

        public static unsafe int GetChars(this Decoder decoder, ReadOnlySpan<byte> bytes, Span<char> chars, bool flush)
        {
            // empty input cannot flush any held-over bytes (verified on netfx), so 0 is correct either way
            if (bytes.IsEmpty) return 0;
            fixed (byte* bPtr = &MemoryMarshal.GetReference(bytes))
            {
                fixed (char* cPtr = &MemoryMarshal.GetReference(chars))
                {
                    return decoder.GetChars(bPtr, bytes.Length, cPtr, chars.Length, flush);
                }
            }
        }

        public static unsafe void Convert(this Decoder decoder, ReadOnlySpan<byte> bytes, Span<char> chars, bool flush, out int bytesUsed, out int charsUsed, out bool completed)
        {
            fixed (char* cPtr = &MemoryMarshal.GetReference(chars))
            {
                if (bytes.IsEmpty)
                {
                    // a valid non-null pointer for the empty-input (flush-only) case
                    byte dummy = 0;
                    decoder.Convert(&dummy, 0, cPtr, chars.Length, flush, out bytesUsed, out charsUsed, out completed);
                }
                else
                {
                    fixed (byte* bPtr = &MemoryMarshal.GetReference(bytes))
                    {
                        decoder.Convert(bPtr, bytes.Length, cPtr, chars.Length, flush, out bytesUsed, out charsUsed, out completed);
                    }
                }
            }
        }
    }
}
#endif
