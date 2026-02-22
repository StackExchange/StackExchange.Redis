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

        Span<byte> keyBuffer = stackalloc byte[CommandBytes.MaxLength];

        while (reader.TryMoveNext() && reader.IsScalar)
        {
            var keyBytes = reader.TryGetSpan(out var tmp) ? tmp : reader.Buffer(keyBuffer);
            if (keyBytes.Length > CommandBytes.MaxLength)
            {
                // Skip this key-value pair
                if (!reader.TryMoveNext()) break;
                continue;
            }

            var hash = AsciiHash.HashCS(keyBytes);

            // Move to value
            if (!reader.TryMoveNext()) break;

            long i64;
            switch (hash)
            {
                case tracking_active.HashCS when tracking_active.IsCS(hash, keyBytes):
                    TrackingActive = reader.ReadBoolean();
                    break;
                case sample_ratio.HashCS when sample_ratio.IsCS(hash, keyBytes) && reader.TryReadInt64(out i64):
                    SampleRatio = i64;
                    break;
                case selected_slots.HashCS when selected_slots.IsCS(hash, keyBytes) && reader.IsAggregate:
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
                case all_commands_all_slots_us.HashCS when all_commands_all_slots_us.IsCS(hash, keyBytes) && reader.TryReadInt64(out i64):
                    AllCommandsAllSlotsMicroseconds = i64;
                    break;
                case all_commands_selected_slots_us.HashCS when all_commands_selected_slots_us.IsCS(hash, keyBytes) && reader.TryReadInt64(out i64):
                    AllCommandSelectedSlotsMicroseconds = i64;
                    break;
                case sampled_command_selected_slots_us.HashCS when sampled_command_selected_slots_us.IsCS(hash, keyBytes) && reader.TryReadInt64(out i64):
                case sampled_commands_selected_slots_us.HashCS when sampled_commands_selected_slots_us.IsCS(hash, keyBytes) && reader.TryReadInt64(out i64):
                    SampledCommandsSelectedSlotsMicroseconds = i64;
                    break;
                case net_bytes_all_commands_all_slots.HashCS when net_bytes_all_commands_all_slots.IsCS(hash, keyBytes) && reader.TryReadInt64(out i64):
                    AllCommandsAllSlotsNetworkBytes = i64;
                    break;
                case net_bytes_all_commands_selected_slots.HashCS when net_bytes_all_commands_selected_slots.IsCS(hash, keyBytes) && reader.TryReadInt64(out i64):
                    NetworkBytesAllCommandsSelectedSlotsRaw = i64;
                    break;
                case net_bytes_sampled_commands_selected_slots.HashCS when net_bytes_sampled_commands_selected_slots.IsCS(hash, keyBytes) && reader.TryReadInt64(out i64):
                    NetworkBytesSampledCommandsSelectedSlotsRaw = i64;
                    break;
                case collection_start_time_unix_ms.HashCS when collection_start_time_unix_ms.IsCS(hash, keyBytes) && reader.TryReadInt64(out i64):
                    CollectionStartTimeUnixMilliseconds = i64;
                    break;
                case collection_duration_ms.HashCS when collection_duration_ms.IsCS(hash, keyBytes) && reader.TryReadInt64(out i64):
                    CollectionDurationMicroseconds = i64 * 1000; // ms vs us is in question: support both, and abstract it from the caller
                    break;
                case collection_duration_us.HashCS when collection_duration_us.IsCS(hash, keyBytes) && reader.TryReadInt64(out i64):
                    CollectionDurationMicroseconds = i64;
                    break;
                case total_cpu_time_sys_ms.HashCS when total_cpu_time_sys_ms.IsCS(hash, keyBytes) && reader.TryReadInt64(out i64):
                    metrics |= HotKeysMetrics.Cpu;
                    TotalCpuTimeSystemMicroseconds = i64 * 1000; // ms vs us is in question: support both, and abstract it from the caller
                    break;
                case total_cpu_time_sys_us.HashCS when total_cpu_time_sys_us.IsCS(hash, keyBytes) && reader.TryReadInt64(out i64):
                    metrics |= HotKeysMetrics.Cpu;
                    TotalCpuTimeSystemMicroseconds = i64;
                    break;
                case total_cpu_time_user_ms.HashCS when total_cpu_time_user_ms.IsCS(hash, keyBytes) && reader.TryReadInt64(out i64):
                    metrics |= HotKeysMetrics.Cpu;
                    TotalCpuTimeUserMicroseconds = i64 * 1000; // ms vs us is in question: support both, and abstract it from the caller
                    break;
                case total_cpu_time_user_us.HashCS when total_cpu_time_user_us.IsCS(hash, keyBytes) && reader.TryReadInt64(out i64):
                    metrics |= HotKeysMetrics.Cpu;
                    TotalCpuTimeUserMicroseconds = i64;
                    break;
                case total_net_bytes.HashCS when total_net_bytes.IsCS(hash, keyBytes) && reader.TryReadInt64(out i64):
                    metrics |= HotKeysMetrics.Network;
                    TotalNetworkBytesRaw = i64;
                    break;
                case by_cpu_time_us.HashCS when by_cpu_time_us.IsCS(hash, keyBytes) && reader.IsAggregate:
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
                case by_net_bytes.HashCS when by_net_bytes.IsCS(hash, keyBytes) && reader.IsAggregate:
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

#pragma warning disable SA1134, SA1300
    // ReSharper disable InconsistentNaming
    [AsciiHash] internal static partial class tracking_active { }
    [AsciiHash] internal static partial class sample_ratio { }
    [AsciiHash] internal static partial class selected_slots { }
    [AsciiHash] internal static partial class all_commands_all_slots_us { }
    [AsciiHash] internal static partial class all_commands_selected_slots_us { }
    [AsciiHash] internal static partial class sampled_command_selected_slots_us { }
    [AsciiHash] internal static partial class sampled_commands_selected_slots_us { }
    [AsciiHash] internal static partial class net_bytes_all_commands_all_slots { }
    [AsciiHash] internal static partial class net_bytes_all_commands_selected_slots { }
    [AsciiHash] internal static partial class net_bytes_sampled_commands_selected_slots { }
    [AsciiHash] internal static partial class collection_start_time_unix_ms { }
    [AsciiHash] internal static partial class collection_duration_ms { }
    [AsciiHash] internal static partial class collection_duration_us { }
    [AsciiHash] internal static partial class total_cpu_time_user_ms { }
    [AsciiHash] internal static partial class total_cpu_time_user_us { }
    [AsciiHash] internal static partial class total_cpu_time_sys_ms { }
    [AsciiHash] internal static partial class total_cpu_time_sys_us { }
    [AsciiHash] internal static partial class total_net_bytes { }
    [AsciiHash] internal static partial class by_cpu_time_us { }
    [AsciiHash] internal static partial class by_net_bytes { }

    // ReSharper restore InconsistentNaming
#pragma warning restore SA1134, SA1300
}
