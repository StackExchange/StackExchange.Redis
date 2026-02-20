using RESPite;

namespace StackExchange.Redis;

public sealed partial class HotKeysResult
{
    internal static readonly ResultProcessor<HotKeysResult?> Processor = new HotKeysResultProcessor();

    private sealed class HotKeysResultProcessor : ResultProcessor<HotKeysResult?>
    {
        protected override bool SetResultCore(PhysicalConnection connection, Message message, RawResult result)
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
        var metrics = HotKeysMetrics.None; // we infer this from the keys present
        var iter = result.GetItems().GetEnumerator();
        while (iter.MoveNext())
        {
            ref readonly RawResult key = ref iter.Current;
            if (!iter.MoveNext()) break; // lies about the length!
            ref readonly RawResult value = ref iter.Current;
            var keyBytes = key.GetBlob();
            if (keyBytes is null) continue;
            var hash = FastHash.HashCS(keyBytes);
            long i64;
            switch (hash)
            {
                case tracking_active.HashCS when tracking_active.IsCS(hash, keyBytes):
                    TrackingActive = value.GetBoolean();
                    break;
                case sample_ratio.HashCS when sample_ratio.IsCS(hash, keyBytes) && value.TryGetInt64(out i64):
                    SampleRatio = i64;
                    break;
                case selected_slots.HashCS when selected_slots.IsCS(hash, keyBytes) & value.Resp2TypeArray is ResultType.Array:
                    var len = value.ItemsCount;
                    if (len == 0)
                    {
                        _selectedSlots = [];
                        continue;
                    }

                    var items = value.GetItems().GetEnumerator();
                    var slots = len == 1 ? null : new SlotRange[len];
                    for (int i = 0; i < len && items.MoveNext(); i++)
                    {
                        ref readonly RawResult pair = ref items.Current;
                        if (pair.Resp2TypeArray is ResultType.Array)
                        {
                            long from = -1, to = -1;
                            switch (pair.ItemsCount)
                            {
                                case 1 when pair[0].TryGetInt64(out from):
                                    to = from; // single slot
                                    break;
                                case 2 when pair[0].TryGetInt64(out from) && pair[1].TryGetInt64(out to):
                                    break;
                            }

                            if (from < SlotRange.MinSlot)
                            {
                                // skip invalid ranges
                            }
                            else if (len == 1 & from == SlotRange.MinSlot & to == SlotRange.MaxSlot)
                            {
                                // this is the "normal" case when no slot filter was applied
                                slots = SlotRange.SharedAllSlots; // avoid the alloc
                            }
                            else
                            {
                                slots ??= new SlotRange[len];
                                slots[i] = new((int)from, (int)to);
                            }
                        }
                    }
                    _selectedSlots = slots;
                    break;
                case all_commands_all_slots_us.HashCS when all_commands_all_slots_us.IsCS(hash, keyBytes) && value.TryGetInt64(out i64):
                    AllCommandsAllSlotsMicroseconds = i64;
                    break;
                case all_commands_selected_slots_us.HashCS when all_commands_selected_slots_us.IsCS(hash, keyBytes) && value.TryGetInt64(out i64):
                    AllCommandSelectedSlotsMicroseconds = i64;
                    break;
                case sampled_command_selected_slots_us.HashCS when sampled_command_selected_slots_us.IsCS(hash, keyBytes) && value.TryGetInt64(out i64):
                case sampled_commands_selected_slots_us.HashCS when sampled_commands_selected_slots_us.IsCS(hash, keyBytes) && value.TryGetInt64(out i64):
                    SampledCommandsSelectedSlotsMicroseconds = i64;
                    break;
                case net_bytes_all_commands_all_slots.HashCS when net_bytes_all_commands_all_slots.IsCS(hash, keyBytes) && value.TryGetInt64(out i64):
                    AllCommandsAllSlotsNetworkBytes = i64;
                    break;
                case net_bytes_all_commands_selected_slots.HashCS when net_bytes_all_commands_selected_slots.IsCS(hash, keyBytes) && value.TryGetInt64(out i64):
                    NetworkBytesAllCommandsSelectedSlotsRaw = i64;
                    break;
                case net_bytes_sampled_commands_selected_slots.HashCS when net_bytes_sampled_commands_selected_slots.IsCS(hash, keyBytes) && value.TryGetInt64(out i64):
                    NetworkBytesSampledCommandsSelectedSlotsRaw = i64;
                    break;
                case collection_start_time_unix_ms.HashCS when collection_start_time_unix_ms.IsCS(hash, keyBytes) && value.TryGetInt64(out i64):
                    CollectionStartTimeUnixMilliseconds = i64;
                    break;
                case collection_duration_ms.HashCS when collection_duration_ms.IsCS(hash, keyBytes) && value.TryGetInt64(out i64):
                    CollectionDurationMicroseconds = i64 * 1000; // ms vs us is in question: support both, and abstract it from the caller
                    break;
                case collection_duration_us.HashCS when collection_duration_us.IsCS(hash, keyBytes) && value.TryGetInt64(out i64):
                    CollectionDurationMicroseconds = i64;
                    break;
                case total_cpu_time_sys_ms.HashCS when total_cpu_time_sys_ms.IsCS(hash, keyBytes) && value.TryGetInt64(out i64):
                    metrics |= HotKeysMetrics.Cpu;
                    TotalCpuTimeSystemMicroseconds = i64 * 1000; // ms vs us is in question: support both, and abstract it from the caller
                    break;
                case total_cpu_time_sys_us.HashCS when total_cpu_time_sys_us.IsCS(hash, keyBytes) && value.TryGetInt64(out i64):
                    metrics |= HotKeysMetrics.Cpu;
                    TotalCpuTimeSystemMicroseconds = i64;
                    break;
                case total_cpu_time_user_ms.HashCS when total_cpu_time_user_ms.IsCS(hash, keyBytes) && value.TryGetInt64(out i64):
                    metrics |= HotKeysMetrics.Cpu;
                    TotalCpuTimeUserMicroseconds = i64 * 1000; // ms vs us is in question: support both, and abstract it from the caller
                    break;
                case total_cpu_time_user_us.HashCS when total_cpu_time_user_us.IsCS(hash, keyBytes) && value.TryGetInt64(out i64):
                    metrics |= HotKeysMetrics.Cpu;
                    TotalCpuTimeUserMicroseconds = i64;
                    break;
                case total_net_bytes.HashCS when total_net_bytes.IsCS(hash, keyBytes) && value.TryGetInt64(out i64):
                    metrics |= HotKeysMetrics.Network;
                    TotalNetworkBytesRaw = i64;
                    break;
                case by_cpu_time_us.HashCS when by_cpu_time_us.IsCS(hash, keyBytes) & value.Resp2TypeArray is ResultType.Array:
                    metrics |= HotKeysMetrics.Cpu;
                    len = value.ItemsCount / 2;
                    if (len == 0)
                    {
                        _cpuByKey = [];
                        continue;
                    }

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

                    _cpuByKey = cpuTime;
                    break;
                case by_net_bytes.HashCS when by_net_bytes.IsCS(hash, keyBytes) & value.Resp2TypeArray is ResultType.Array:
                    metrics |= HotKeysMetrics.Network;
                    len = value.ItemsCount / 2;
                    if (len == 0)
                    {
                        _networkBytesByKey = [];
                        continue;
                    }

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

                    _networkBytesByKey = netBytes;
                    break;
            } // switch
        } // while
        Metrics = metrics;
    }

#pragma warning disable SA1134, SA1300
    // ReSharper disable InconsistentNaming
    [FastHash] internal static partial class tracking_active { }
    [FastHash] internal static partial class sample_ratio { }
    [FastHash] internal static partial class selected_slots { }
    [FastHash] internal static partial class all_commands_all_slots_us { }
    [FastHash] internal static partial class all_commands_selected_slots_us { }
    [FastHash] internal static partial class sampled_command_selected_slots_us { }
    [FastHash] internal static partial class sampled_commands_selected_slots_us { }
    [FastHash] internal static partial class net_bytes_all_commands_all_slots { }
    [FastHash] internal static partial class net_bytes_all_commands_selected_slots { }
    [FastHash] internal static partial class net_bytes_sampled_commands_selected_slots { }
    [FastHash] internal static partial class collection_start_time_unix_ms { }
    [FastHash] internal static partial class collection_duration_ms { }
    [FastHash] internal static partial class collection_duration_us { }
    [FastHash] internal static partial class total_cpu_time_user_ms { }
    [FastHash] internal static partial class total_cpu_time_user_us { }
    [FastHash] internal static partial class total_cpu_time_sys_ms { }
    [FastHash] internal static partial class total_cpu_time_sys_us { }
    [FastHash] internal static partial class total_net_bytes { }
    [FastHash] internal static partial class by_cpu_time_us { }
    [FastHash] internal static partial class by_net_bytes { }

    // ReSharper restore InconsistentNaming
#pragma warning restore SA1134, SA1300
}
