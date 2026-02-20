using RESPite;

namespace StackExchange.Redis;

internal partial class ResultProcessor
{
    internal partial class Literals
    {
#pragma warning disable CS8981, SA1300, SA1134 // forgive naming etc
        // ReSharper disable InconsistentNaming
        [FastHash] internal static partial class NOAUTH { }
        [FastHash] internal static partial class WRONGPASS { }
        [FastHash] internal static partial class NOSCRIPT { }
        [FastHash] internal static partial class MOVED { }
        [FastHash] internal static partial class ASK { }
        [FastHash] internal static partial class READONLY { }
        [FastHash] internal static partial class LOADING { }
        [FastHash("ERR operation not permitted")]
        internal static partial class ERR_not_permitted { }

        [FastHash] internal static partial class length { }
        [FastHash] internal static partial class radix_tree_keys { }
        [FastHash] internal static partial class radix_tree_nodes { }
        [FastHash] internal static partial class last_generated_id { }
        [FastHash] internal static partial class max_deleted_entry_id { }
        [FastHash] internal static partial class entries_added { }
        [FastHash] internal static partial class recorded_first_entry_id { }
        [FastHash] internal static partial class idmp_duration { }
        [FastHash] internal static partial class idmp_maxsize { }
        [FastHash] internal static partial class pids_tracked { }
        [FastHash] internal static partial class first_entry { }
        [FastHash] internal static partial class last_entry { }
        [FastHash] internal static partial class groups { }
        [FastHash] internal static partial class iids_tracked { }
        [FastHash] internal static partial class iids_added { }
        [FastHash] internal static partial class iids_duplicates { }

        // Role types
        [FastHash] internal static partial class master { }
        [FastHash] internal static partial class slave { }
        [FastHash] internal static partial class replica { }
        [FastHash] internal static partial class sentinel { }

        // Replication states
        [FastHash] internal static partial class connect { }
        [FastHash] internal static partial class connecting { }
        [FastHash] internal static partial class sync { }
        [FastHash] internal static partial class connected { }
        [FastHash] internal static partial class none { }
        [FastHash] internal static partial class handshake { }

        // Result processor literals
        [FastHash]
        internal static partial class OK
        {
            public static readonly FastHash Hash = new(U8);
        }

        [FastHash]
        internal static partial class PONG
        {
            public static readonly FastHash Hash = new(U8);
        }

        [FastHash("Background saving started")]
        internal static partial class background_saving_started
        {
            public static readonly FastHash Hash = new(U8);
        }

        [FastHash("Background append only file rewriting started")]
        internal static partial class background_aof_rewriting_started
        {
            public static readonly FastHash Hash = new(U8);
        }

        // LCS processor literals
        [FastHash] internal static partial class matches { }
        [FastHash] internal static partial class len { }

        // Sentinel processor literals
        [FastHash] internal static partial class ip { }
        [FastHash] internal static partial class port { }
        // ReSharper restore InconsistentNaming
#pragma warning restore CS8981, SA1300, SA1134 // forgive naming etc
    }
}
