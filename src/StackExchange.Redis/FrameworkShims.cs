#pragma warning disable SA1403 // single namespace

#if NET5_0_OR_GREATER
// context: https://github.com/StackExchange/StackExchange.Redis/issues/2619
[assembly: System.Runtime.CompilerServices.TypeForwardedTo(typeof(System.Runtime.CompilerServices.IsExternalInit))]
#else
// To support { get; init; } properties
using System.ComponentModel;
using System.Text;

namespace System.Runtime.CompilerServices
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    internal static class IsExternalInit { }
}
#endif

#if !(NETCOREAPP || NETSTANDARD2_1_OR_GREATER)

namespace System.Text
{
    internal static unsafe class EncodingExtensions
    {
        public static int GetBytes(this Encoding encoding, ReadOnlySpan<char> source, Span<byte> destination)
        {
            fixed (byte* bPtr = destination)
            {
                fixed (char* cPtr = source)
                {
                    return encoding.GetBytes(cPtr, source.Length, bPtr, destination.Length);
                }
            }
        }
        public static string GetString(this Encoding encoding, ReadOnlySpan<byte> source)
        {
            fixed (byte* bPtr = source)
            {
                return encoding.GetString(bPtr, source.Length);
            }
        }
        public static int GetChars(this Encoding encoding, ReadOnlySpan<byte> source, Span<char> destination)
        {
            fixed (byte* bPtr = source)
            {
                fixed (char* cPtr = destination)
                {
                    return encoding.GetChars(bPtr, source.Length, cPtr, destination.Length);
                }
            }
        }

        public static int GetByteCount(this Encoding encoding, ReadOnlySpan<char> source)
        {
            fixed (char* cPtr = source)
            {
                return encoding.GetByteCount(cPtr, source.Length);
            }
        }

        public static void Convert(this Encoder encoder, ReadOnlySpan<char> source, Span<byte> destination, bool flush, out int charsUsed, out int bytesUsed, out bool completed)
        {
            fixed (char* cPtr = source)
            {
                fixed (byte* bPtr = destination)
                {
                    encoder.Convert(cPtr, source.Length, bPtr, destination.Length, flush, out charsUsed, out bytesUsed, out completed);
                }
            }
        }
    }
}
#endif


#pragma warning restore SA1403
