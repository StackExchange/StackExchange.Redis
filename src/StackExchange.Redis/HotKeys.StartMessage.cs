using System;
using System.Threading.Tasks;

namespace StackExchange.Redis;

internal partial class RedisServer
{
    internal sealed class HotKeysStartMessage(
        CommandFlags flags,
        HotKeysMetrics metrics,
        long count,
        TimeSpan duration,
        long sampleRatio,
        short[]? slots) : Message(-1, flags, RedisCommand.HOTKEYS)
    {
        protected override void WriteImpl(PhysicalConnection physical)
        {
            /*
           HOTKEYS
               <METRICS count [CPU] [NET]>
               [COUNT k]
               [DURATION duration]
               [SAMPLE ratio]
               [SLOTS count slot…]
           */
            physical.WriteHeader(Command, ArgCount);
            physical.WriteBulkString("METRICS"u8);
            var metricCount = 0;
            if ((metrics & HotKeysMetrics.Cpu) != 0) metricCount++;
            if ((metrics & HotKeysMetrics.Network) != 0) metricCount++;
            physical.WriteBulkString(metricCount);
            if ((metrics & HotKeysMetrics.Cpu) != 0) physical.WriteBulkString("CPU"u8);
            if ((metrics & HotKeysMetrics.Network) != 0) physical.WriteBulkString("NET"u8);

            if (count != 0)
            {
                physical.WriteBulkString("COUNT"u8);
                physical.WriteBulkString(count);
            }

            if (duration != TimeSpan.Zero)
            {
                physical.WriteBulkString("DURATION"u8);
                physical.WriteBulkString(Math.Ceiling(duration.TotalSeconds));
            }

            if (sampleRatio != 0)
            {
                physical.WriteBulkString("SAMPLE"u8);
                physical.WriteBulkString(sampleRatio);
            }

            if (slots is { Length: > 0 })
            {
                physical.WriteBulkString("SLOTS"u8);
                physical.WriteBulkString(slots.Length);
                foreach (var slot in slots)
                {
                    physical.WriteBulkString(slot);
                }
            }
        }

        public override int ArgCount
        {
            get
            {
                int argCount = 2;
                if ((metrics & HotKeysMetrics.Cpu) != 0) argCount++;
                if ((metrics & HotKeysMetrics.Network) != 0) argCount++;
                if (count != 0) argCount += 2;
                if (duration != TimeSpan.Zero) argCount += 2;
                if (sampleRatio != 0) argCount += 2;
                if (slots is { Length: > 0 }) argCount += 2 + slots.Length;
                return argCount;
            }
        }
    }
}
