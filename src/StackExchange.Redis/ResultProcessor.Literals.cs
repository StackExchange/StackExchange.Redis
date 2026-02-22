using RESPite;

namespace StackExchange.Redis;

internal partial class ResultProcessor
{
    internal partial class Literals
    {
#pragma warning disable CS8981, SA1300, SA1134 // forgive naming etc
        // ReSharper disable InconsistentNaming
        [AsciiHash] internal static partial class NOAUTH { }
        [AsciiHash] internal static partial class WRONGPASS { }
        [AsciiHash] internal static partial class NOSCRIPT { }
        [AsciiHash] internal static partial class MOVED { }
        [AsciiHash] internal static partial class ASK { }
        [AsciiHash] internal static partial class READONLY { }
        [AsciiHash] internal static partial class LOADING { }
        [AsciiHash("ERR operation not permitted")]
        internal static partial class ERR_not_permitted { }

        [AsciiHash] internal static partial class length { }
        [AsciiHash] internal static partial class radix_tree_keys { }
        [AsciiHash] internal static partial class radix_tree_nodes { }
        [AsciiHash] internal static partial class last_generated_id { }
        [AsciiHash] internal static partial class max_deleted_entry_id { }
        [AsciiHash] internal static partial class entries_added { }
        [AsciiHash] internal static partial class recorded_first_entry_id { }
        [AsciiHash] internal static partial class idmp_duration { }
        [AsciiHash] internal static partial class idmp_maxsize { }
        [AsciiHash] internal static partial class pids_tracked { }
        [AsciiHash] internal static partial class first_entry { }
        [AsciiHash] internal static partial class last_entry { }
        [AsciiHash] internal static partial class groups { }
        [AsciiHash] internal static partial class iids_tracked { }
        [AsciiHash] internal static partial class iids_added { }
        [AsciiHash] internal static partial class iids_duplicates { }

        // Role types
        [AsciiHash] internal static partial class master { }
        [AsciiHash] internal static partial class slave { }
        [AsciiHash] internal static partial class replica { }
        [AsciiHash] internal static partial class sentinel { }
        [AsciiHash] internal static partial class primary { }
        [AsciiHash] internal static partial class standalone { }
        [AsciiHash] internal static partial class cluster { }

        // Config keys
        [AsciiHash] internal static partial class timeout { }
        [AsciiHash] internal static partial class databases { }
        [AsciiHash("slave-read-only")] internal static partial class slave_read_only { }
        [AsciiHash("replica-read-only")] internal static partial class replica_read_only { }
        [AsciiHash] internal static partial class yes { }
        [AsciiHash] internal static partial class no { }

        // HELLO keys
        [AsciiHash] internal static partial class version { }
        [AsciiHash] internal static partial class proto { }
        [AsciiHash] internal static partial class id { }
        [AsciiHash] internal static partial class mode { }
        [AsciiHash] internal static partial class role { }

        // Replication states
        [AsciiHash] internal static partial class connect { }
        [AsciiHash] internal static partial class connecting { }
        [AsciiHash] internal static partial class sync { }
        [AsciiHash] internal static partial class connected { }
        [AsciiHash] internal static partial class none { }
        [AsciiHash] internal static partial class handshake { }

        // Result processor literals
        [AsciiHash]
        internal static partial class OK
        {
            public static readonly AsciiHash Hash = new(U8);
        }

        [AsciiHash]
        internal static partial class PONG
        {
            public static readonly AsciiHash Hash = new(U8);
        }

        [AsciiHash("Background saving started")]
        internal static partial class background_saving_started
        {
            public static readonly AsciiHash Hash = new(U8);
        }

        [AsciiHash("Background append only file rewriting started")]
        internal static partial class background_aof_rewriting_started
        {
            public static readonly AsciiHash Hash = new(U8);
        }

        // LCS processor literals
        [AsciiHash] internal static partial class matches { }
        [AsciiHash] internal static partial class len { }

        // Sentinel processor literals
        [AsciiHash] internal static partial class ip { }
        [AsciiHash] internal static partial class port { }

        // Stream info processor literals
        [AsciiHash] internal static partial class name { }
        [AsciiHash] internal static partial class pending { }
        [AsciiHash] internal static partial class idle { }
        [AsciiHash] internal static partial class consumers { }
        [AsciiHash("last-delivered-id")] internal static partial class last_delivered_id { }
        [AsciiHash("entries-read")] internal static partial class entries_read { }
        [AsciiHash] internal static partial class lag { }
        // ReSharper restore InconsistentNaming
#pragma warning restore CS8981, SA1300, SA1134 // forgive naming etc
    }
}
