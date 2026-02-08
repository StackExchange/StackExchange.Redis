namespace StackExchange.Redis;

public sealed partial class HotKeysResult
{
    internal static readonly ResultProcessor<HotKeysResult?> Processor = new HotKeysResultProcessor();

    private sealed class HotKeysResultProcessor : ResultProcessor<HotKeysResult?>
    {
        protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
        {
            if (result.IsNull)
            {
                SetResult(message, null);
                return true;
            }

            // an array with a single element that *is* an array/map that is the results
            if (result is { Resp2TypeArray: ResultType.Array, ItemsCount: 1 })
            {
                ref readonly RawResult inner = ref result[0];
                if (inner is { Resp2TypeArray: ResultType.Array, IsNull: false })
                {
                    var hotKeys = new HotKeysResult(in inner);
                    SetResult(message, hotKeys);
                    return true;
                }
            }

            return false;
        }
    }

    private HotKeysResult(in RawResult result)
    {
        var iter = result.GetItems().GetEnumerator();
        while (iter.MoveNext())
        {
            ref readonly RawResult key = ref iter.Current;
            if (iter.MoveNext())
            {
                ref readonly RawResult value = ref iter.Current;
                var hash = key.Payload.Hash64();
                switch (hash)
                {
                    case tracking_active.Hash when tracking_active.Is(hash, key):
                        TrackingActive = value.GetBoolean();
                        break;
                    case sample_ratio.Hash when sample_ratio.Is(hash, key) && value.TryGetInt64(out var i64):
                        SampleRatio = i64;
                        break;
                    case selected_slots.Hash when selected_slots.Is(hash, key) & value.Resp2TypeArray is ResultType.Array:
                        var len = value.ItemsCount;
                        if (len == 0) continue;

                        var items = value.GetItems().GetEnumerator();
                        var slots = new SlotRange[len];
                        for (int i = 0; i < len && items.MoveNext(); i++)
                        {
                            ref readonly RawResult pair = ref items.Current;
                            if (pair.Resp2TypeArray is ResultType.Array
                                && pair.ItemsCount == 2
                                && pair[0].TryGetInt64(out var from)
                                && pair[1].TryGetInt64(out var to))
                            {
                                slots[i] = new((int)from, (int)to);
                            }
                        }
                        SelectedSlots = slots;
                        break;
                    case all_commands_all_slots_us.Hash when all_commands_all_slots_us.Is(hash, key) && value.TryGetInt64(out var i64):
                        TotalCpuTimeMilliseconds = i64;
                        break;
                    case net_bytes_all_commands_all_slots.Hash when net_bytes_all_commands_all_slots.Is(hash, key) && value.TryGetInt64(out var i64):
                        TotalNetworkBytes = i64;
                        break;
                    case collection_start_time_unix_ms.Hash when collection_start_time_unix_ms.Is(hash, key) && value.TryGetInt64(out var i64):
                        CollectionStartTimeUnixMilliseconds = i64;
                        break;
                    case collection_duration_ms.Hash when collection_duration_ms.Is(hash, key) && value.TryGetInt64(out var i64):
                        CollectionDurationMilliseconds = i64;
                        break;
                    case total_cpu_time_sys_ms.Hash when total_cpu_time_sys_ms.Is(hash, key) && value.TryGetInt64(out var i64):
                        TotalCpuTimeSystemMilliseconds = i64;
                        break;
                    case total_cpu_time_user_ms.Hash when total_cpu_time_user_ms.Is(hash, key) && value.TryGetInt64(out var i64):
                        TotalCpuTimeUserMilliseconds = i64;
                        break;
                    case total_net_bytes.Hash when total_net_bytes.Is(hash, key) && value.TryGetInt64(out var i64):
                        TotalNetworkBytes2 = i64;
                        break;
                    case by_cpu_time_us.Hash when by_cpu_time_us.Is(hash, key) & value.Resp2TypeArray is ResultType.Array:
                        len = value.ItemsCount / 2;
                        if (len == 0) continue;

                        var cpuTime = new MetricKeyCpu[len];
                        items = value.GetItems().GetEnumerator();
                        for (int i = 0; i < len && items.MoveNext(); i++)
                        {
                            var metricKey = items.Current.AsRedisKey();
                            if (items.MoveNext() && items.Current.TryGetInt64(out var metricValue))
                            {
                                cpuTime[i] = new(metricKey, metricValue);
                            }
                        }

                        CpuByKey = cpuTime;
                        break;
                    case by_net_bytes.Hash when by_net_bytes.Is(hash, key) & value.Resp2TypeArray is ResultType.Array:
                        len = value.ItemsCount / 2;
                        if (len == 0) continue;

                        var netBytes = new MetricKeyBytes[len];
                        items = value.GetItems().GetEnumerator();
                        for (int i = 0; i < len && items.MoveNext(); i++)
                        {
                            var metricKey = items.Current.AsRedisKey();
                            if (items.MoveNext() && items.Current.TryGetInt64(out var metricValue))
                            {
                                netBytes[i] = new(metricKey, metricValue);
                            }
                        }

                        NetworkBytesByKey = netBytes;
                        break;
                }
            }
        }
    }

#pragma warning disable SA1134, SA1300
    // ReSharper disable InconsistentNaming
    [FastHash] internal static partial class tracking_active { }
    [FastHash] internal static partial class sample_ratio { }
    [FastHash] internal static partial class selected_slots { }
    [FastHash] internal static partial class all_commands_all_slots_us { }
    [FastHash] internal static partial class net_bytes_all_commands_all_slots { }
    [FastHash] internal static partial class collection_start_time_unix_ms { }
    [FastHash] internal static partial class collection_duration_ms { }
    [FastHash] internal static partial class total_cpu_time_user_ms { }
    [FastHash] internal static partial class total_cpu_time_sys_ms { }
    [FastHash] internal static partial class total_net_bytes { }
    [FastHash] internal static partial class by_cpu_time_us { }
    [FastHash] internal static partial class by_net_bytes { }

    // ReSharper restore InconsistentNaming
#pragma warning restore SA1134, SA1300
}
