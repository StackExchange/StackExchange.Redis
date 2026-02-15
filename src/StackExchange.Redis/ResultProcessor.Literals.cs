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

        // ReSharper restore InconsistentNaming
#pragma warning restore CS8981, SA1300, SA1134 // forgive naming etc
    }
}
