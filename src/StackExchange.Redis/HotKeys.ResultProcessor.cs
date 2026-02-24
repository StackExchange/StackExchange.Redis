using System;
using RESPite;
using RESPite.Messages;

namespace StackExchange.Redis;

public sealed partial class HotKeysResult
{
    internal static readonly ResultProcessor<HotKeysResult?> Processor = new HotKeysResultProcessor();

    private sealed class HotKeysResultProcessor : ResultProcessor<HotKeysResult?>
    {
        protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
        {
            if (reader.IsNull)
            {
                SetResult(message, null);
                return true;
            }

            // an array with a single element that *is* an array/map that is the results
            if (reader.IsAggregate && reader.AggregateLengthIs(1))
            {
                var iter = reader.AggregateChildren();
                iter.DemandNext();
                if (iter.Value.IsAggregate && !iter.Value.IsNull)
                {
                    var hotKeys = new HotKeysResult(ref iter.Value);
                    SetResult(message, hotKeys);
                    return true;
                }
            }

            return false;
        }
    }

    private HotKeysResult(ref RespReader reader)
    {
        var metrics = HotKeysMetrics.None; // we infer this from the keys present
        int count = reader.AggregateLength();
        if ((count & 1) != 0) return; // must be even (key-value pairs)

        while (reader.TryMoveNext() && reader.IsScalar)
        {
            if (!reader.TryRead(HotKeysFieldMetadata.TryParse, out HotKeysField field))
            {
                field = HotKeysField.Unknown;
            }

            // Move to value
            if (!reader.TryMoveNext()) break;

            long i64;
            switch (field)
            {
                case HotKeysField.TrackingActive:
                    TrackingActive = reader.ReadBoolean();
                    break;
                case HotKeysField.SampleRatio when reader.TryReadInt64(out i64):
                    SampleRatio = i64;
                    break;
                case HotKeysField.SelectedSlots when reader.IsAggregate:
                    var slotRanges = reader.ReadPastArray(
                        static (ref RespReader slotReader) =>
                        {
                            if (!slotReader.IsAggregate) return default;

                            int pairLen = slotReader.AggregateLength();
                            long from = -1, to = -1;

                            var pairIter = slotReader.AggregateChildren();
                            if (pairLen >= 1 && pairIter.MoveNext() && pairIter.Value.TryReadInt64(out from))
                            {
                                to = from; // single slot
                                if (pairLen >= 2 && pairIter.MoveNext() && pairIter.Value.TryReadInt64(out to))
                                {
                                    // to is now set
                                }
                            }

                            return from >= SlotRange.MinSlot ? new SlotRange((int)from, (int)to) : default;
                        },
                        scalar: false);

                    if (slotRanges is { Length: 1 } && slotRanges[0].From == SlotRange.MinSlot && slotRanges[0].To == SlotRange.MaxSlot)
                    {
                        // this is the "normal" case when no slot filter was applied
                        _selectedSlots = SlotRange.SharedAllSlots; // avoid the alloc
                    }
                    else
                    {
                        _selectedSlots = slotRanges ?? [];
                    }
                    break;
                case HotKeysField.AllCommandsAllSlotsUs when reader.TryReadInt64(out i64):
                    AllCommandsAllSlotsMicroseconds = i64;
                    break;
                case HotKeysField.AllCommandsSelectedSlotsUs when reader.TryReadInt64(out i64):
                    AllCommandSelectedSlotsMicroseconds = i64;
                    break;
                case HotKeysField.SampledCommandSelectedSlotsUs when reader.TryReadInt64(out i64):
                case HotKeysField.SampledCommandsSelectedSlotsUs when reader.TryReadInt64(out i64):
                    SampledCommandsSelectedSlotsMicroseconds = i64;
                    break;
                case HotKeysField.NetBytesAllCommandsAllSlots when reader.TryReadInt64(out i64):
                    AllCommandsAllSlotsNetworkBytes = i64;
                    break;
                case HotKeysField.NetBytesAllCommandsSelectedSlots when reader.TryReadInt64(out i64):
                    NetworkBytesAllCommandsSelectedSlotsRaw = i64;
                    break;
                case HotKeysField.NetBytesSampledCommandsSelectedSlots when reader.TryReadInt64(out i64):
                    NetworkBytesSampledCommandsSelectedSlotsRaw = i64;
                    break;
                case HotKeysField.CollectionStartTimeUnixMs when reader.TryReadInt64(out i64):
                    CollectionStartTimeUnixMilliseconds = i64;
                    break;
                case HotKeysField.CollectionDurationMs when reader.TryReadInt64(out i64):
                    CollectionDurationMicroseconds = i64 * 1000; // ms vs us is in question: support both, and abstract it from the caller
                    break;
                case HotKeysField.CollectionDurationUs when reader.TryReadInt64(out i64):
                    CollectionDurationMicroseconds = i64;
                    break;
                case HotKeysField.TotalCpuTimeSysMs when reader.TryReadInt64(out i64):
                    metrics |= HotKeysMetrics.Cpu;
                    TotalCpuTimeSystemMicroseconds = i64 * 1000; // ms vs us is in question: support both, and abstract it from the caller
                    break;
                case HotKeysField.TotalCpuTimeSysUs when reader.TryReadInt64(out i64):
                    metrics |= HotKeysMetrics.Cpu;
                    TotalCpuTimeSystemMicroseconds = i64;
                    break;
                case HotKeysField.TotalCpuTimeUserMs when reader.TryReadInt64(out i64):
                    metrics |= HotKeysMetrics.Cpu;
                    TotalCpuTimeUserMicroseconds = i64 * 1000; // ms vs us is in question: support both, and abstract it from the caller
                    break;
                case HotKeysField.TotalCpuTimeUserUs when reader.TryReadInt64(out i64):
                    metrics |= HotKeysMetrics.Cpu;
                    TotalCpuTimeUserMicroseconds = i64;
                    break;
                case HotKeysField.TotalNetBytes when reader.TryReadInt64(out i64):
                    metrics |= HotKeysMetrics.Network;
                    TotalNetworkBytesRaw = i64;
                    break;
                case HotKeysField.ByCpuTimeUs when reader.IsAggregate:
                    metrics |= HotKeysMetrics.Cpu;
                    int cpuLen = reader.AggregateLength() / 2;
                    var cpuTime = new MetricKeyCpu[cpuLen];
                    var cpuIter = reader.AggregateChildren();
                    int cpuIdx = 0;
                    while (cpuIter.MoveNext() && cpuIdx < cpuLen)
                    {
                        var metricKey = cpuIter.Value.ReadRedisKey();
                        if (cpuIter.MoveNext() && cpuIter.Value.TryReadInt64(out var metricValue))
                        {
                            cpuTime[cpuIdx++] = new(metricKey, metricValue);
                        }
                    }
                    _cpuByKey = cpuTime;
                    break;
                case HotKeysField.ByNetBytes when reader.IsAggregate:
                    metrics |= HotKeysMetrics.Network;
                    int netLen = reader.AggregateLength() / 2;
                    var netBytes = new MetricKeyBytes[netLen];
                    var netIter = reader.AggregateChildren();
                    int netIdx = 0;
                    while (netIter.MoveNext() && netIdx < netLen)
                    {
                        var metricKey = netIter.Value.ReadRedisKey();
                        if (netIter.MoveNext() && netIter.Value.TryReadInt64(out var metricValue))
                        {
                            netBytes[netIdx++] = new(metricKey, metricValue);
                        }
                    }
                    _networkBytesByKey = netBytes;
                    break;
            } // switch
        } // while
        Metrics = metrics;
    }
}
