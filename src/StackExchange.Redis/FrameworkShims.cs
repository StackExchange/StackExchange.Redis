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
    internal static class EncodingExtensions
    {
        public static unsafe int GetBytes(this Encoding encoding, ReadOnlySpan<char> source, Span<byte> destination)
        {
            fixed (byte* bPtr = destination)
            {
                fixed (char* cPtr = source)
                {
                    return encoding.GetBytes(cPtr, source.Length, bPtr, destination.Length);
                }
            }
        }
    }
}
#endif


#pragma warning restore SA1403
