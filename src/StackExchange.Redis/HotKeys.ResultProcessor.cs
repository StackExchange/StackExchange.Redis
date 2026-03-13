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
        var metrics = HotKeysMetrics.None; // we infer this from the keys present
        var iter = result.GetItems().GetEnumerator();
        while (iter.MoveNext())
        {
            if (!iter.Current.TryParse(HotKeysFieldMetadata.TryParse, out HotKeysField field))
                field = HotKeysField.Unknown;

            if (!iter.MoveNext()) break; // lies about the length!
            ref readonly RawResult value = ref iter.Current;

            long i64;
            switch (field)
            {
                case HotKeysField.TrackingActive:
                    TrackingActive = value.GetBoolean();
                    break;
                case HotKeysField.SampleRatio when value.TryGetInt64(out i64):
                    SampleRatio = i64;
                    break;
                case HotKeysField.SelectedSlots when value.Resp2TypeArray is ResultType.Array:
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
                case HotKeysField.AllCommandsAllSlotsUs when value.TryGetInt64(out i64):
                    AllCommandsAllSlotsMicroseconds = i64;
                    break;
                case HotKeysField.AllCommandsSelectedSlotsUs when value.TryGetInt64(out i64):
                    AllCommandSelectedSlotsMicroseconds = i64;
                    break;
                case HotKeysField.SampledCommandSelectedSlotsUs when value.TryGetInt64(out i64):
                case HotKeysField.SampledCommandsSelectedSlotsUs when value.TryGetInt64(out i64):
                    SampledCommandsSelectedSlotsMicroseconds = i64;
                    break;
                case HotKeysField.NetBytesAllCommandsAllSlots when value.TryGetInt64(out i64):
                    AllCommandsAllSlotsNetworkBytes = i64;
                    break;
                case HotKeysField.NetBytesAllCommandsSelectedSlots when value.TryGetInt64(out i64):
                    NetworkBytesAllCommandsSelectedSlotsRaw = i64;
                    break;
                case HotKeysField.NetBytesSampledCommandsSelectedSlots when value.TryGetInt64(out i64):
                    NetworkBytesSampledCommandsSelectedSlotsRaw = i64;
                    break;
                case HotKeysField.CollectionStartTimeUnixMs when value.TryGetInt64(out i64):
                    CollectionStartTimeUnixMilliseconds = i64;
                    break;
                case HotKeysField.CollectionDurationMs when value.TryGetInt64(out i64):
                    CollectionDurationMicroseconds = i64 * 1000; // ms vs us is in question: support both, and abstract it from the caller
                    break;
                case HotKeysField.CollectionDurationUs when value.TryGetInt64(out i64):
                    CollectionDurationMicroseconds = i64;
                    break;
                case HotKeysField.TotalCpuTimeSysMs when value.TryGetInt64(out i64):
                    metrics |= HotKeysMetrics.Cpu;
                    TotalCpuTimeSystemMicroseconds = i64 * 1000; // ms vs us is in question: support both, and abstract it from the caller
                    break;
                case HotKeysField.TotalCpuTimeSysUs when value.TryGetInt64(out i64):
                    metrics |= HotKeysMetrics.Cpu;
                    TotalCpuTimeSystemMicroseconds = i64;
                    break;
                case HotKeysField.TotalCpuTimeUserMs when value.TryGetInt64(out i64):
                    metrics |= HotKeysMetrics.Cpu;
                    TotalCpuTimeUserMicroseconds = i64 * 1000; // ms vs us is in question: support both, and abstract it from the caller
                    break;
                case HotKeysField.TotalCpuTimeUserUs when value.TryGetInt64(out i64):
                    metrics |= HotKeysMetrics.Cpu;
                    TotalCpuTimeUserMicroseconds = i64;
                    break;
                case HotKeysField.TotalNetBytes when value.TryGetInt64(out i64):
                    metrics |= HotKeysMetrics.Network;
                    TotalNetworkBytesRaw = i64;
                    break;
                case HotKeysField.ByCpuTimeUs when value.Resp2TypeArray is ResultType.Array:
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
                case HotKeysField.ByNetBytes when value.Resp2TypeArray is ResultType.Array:
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
}
