#pragma warning disable SA1403 // single namespace

#if RESPITE // add nothing
#elif NET5_0_OR_GREATER
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

#if !NET9_0_OR_GREATER
namespace System.Runtime.CompilerServices
{
    // see https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.overloadresolutionpriorityattribute
    [AttributeUsage(AttributeTargets.Constructor | AttributeTargets.Method | AttributeTargets.Property, Inherited = false)]
    internal sealed class OverloadResolutionPriorityAttribute(int priority) : Attribute
    {
        public int Priority => priority;
    }
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

        public static unsafe int GetChars(this Encoding encoding, ReadOnlySpan<byte> source, Span<char> destination)
        {
            fixed (byte* bPtr = source)
            {
                fixed (char* cPtr = destination)
                {
                    return encoding.GetChars(bPtr, source.Length, cPtr, destination.Length);
                }
            }
        }

        public static unsafe int GetCharCount(this Encoding encoding, ReadOnlySpan<byte> source)
        {
            fixed (byte* bPtr = source)
            {
                return encoding.GetCharCount(bPtr, source.Length);
            }
        }

        public static unsafe string GetString(this Encoding encoding, ReadOnlySpan<byte> source)
        {
            fixed (byte* bPtr = source)
            {
                return encoding.GetString(bPtr, source.Length);
            }
        }
    }
}
#endif


#pragma warning restore SA1403
