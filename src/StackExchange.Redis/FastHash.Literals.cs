using System;
using System.Diagnostics.CodeAnalysis;

namespace StackExchange.Redis;

// See FastHashTests for how these are validated and enforced. When adding new values, use any
// value and run the tests - this will tell you the correct value.
[SuppressMessage("ReSharper", "InconsistentNaming", Justification = "To better represent the expected literals")]
internal static partial class FastHash
{
#pragma warning disable SA1300, SA1303
    public static class _3
    {
        public const long bin = 7235938;
        public static ReadOnlySpan<byte> bin_u8 => "bin"u8;

        public const long f32 = 3289958;
        public static ReadOnlySpan<byte> f32_u8 => "f32"u8;
    }

    public static class _4
    {
        public const long size = 1702521203;
        public static ReadOnlySpan<byte> size_u8 => "size"u8;

        public const long int8 = 947154537;
        public static ReadOnlySpan<byte> int8_u8 => "int8"u8;
    }

    public static class _8
    {
        public const long vset_uid = 7235443114434196342;
        public static ReadOnlySpan<byte> vset_uid_u8 => "vset-uid"u8;
    }

    public static class _9
    {
        public const long max_level = 7311142560376316269;
        public static ReadOnlySpan<byte> max_level_u8 => "max-level"u8;
    }

    public static class _10
    {
        public const long quant_type = 8751669953979053425;
        public static ReadOnlySpan<byte> quant_type_u8 => "quant-type"u8;

        public const long vector_dim = 7218551600764380534;
        public static ReadOnlySpan<byte> vector_dim_u8 => "vector-dim"u8;
    }

    public static class _17
    {
        public const long hnsw_max_node_uid = 8674334399337295464;
        public static ReadOnlySpan<byte> hnsw_max_node_uid_u8 => "hnsw-max-node-uid"u8;
    }
#pragma warning restore SA1300, SA1303
}
