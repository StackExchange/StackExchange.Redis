using System;
using System.Diagnostics.CodeAnalysis;

namespace StackExchange.Redis;

// See FastHashTests for how these are validated and enforced. When adding new values, use any
// value and run the tests - this will tell you the correct value.
[SuppressMessage("ReSharper", "InconsistentNaming", Justification = "To better represent the expected literals")]
internal static partial class FastHash
{
#pragma warning disable SA1300, SA1303
    public static class Length4
    {
        public const long size = 1702521203;
        public static ReadOnlySpan<byte> size_u8 => "size"u8;
    }
#pragma warning restore SA1300, SA1303
}
