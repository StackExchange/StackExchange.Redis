using System.Diagnostics.CodeAnalysis;

namespace StackExchange.Redis;

[SuppressMessage("ReSharper", "InconsistentNaming", Justification = "To better represent the expected literals")]
internal static partial class FastHash
{
    // see HastHashGenerator.md for more information and intended usage.
#pragma warning disable CS8981, SA1134, SA1300, SA1303, SA1502
    [FastHash] public static partial class bin { }
    [FastHash] public static partial class f32 { }
    [FastHash] public static partial class int8 { }
    [FastHash] public static partial class size { }
    [FastHash] public static partial class vset_uid { }
    [FastHash] public static partial class max_level { }
    [FastHash] public static partial class quant_type { }
    [FastHash] public static partial class vector_dim { }
    [FastHash] public static partial class hnsw_max_node_uid { }
#pragma warning restore CS8981, SA1134, SA1300, SA1303, SA1502
}
