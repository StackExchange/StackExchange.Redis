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
        int[]? slots) : Message(-1, flags, RedisCommand.HOTKEYS)
    {
        protected override void WriteImpl(in MessageWriter writer)
        {
            /*
           HOTKEYS START
               <METRICS count [CPU] [NET]>
               [COUNT k]
               [DURATION duration]
               [SAMPLE ratio]
               [SLOTS count slot…]
           */
            writer.WriteHeader(Command, ArgCount);
            writer.WriteBulkString("START"u8);
            writer.WriteBulkString("METRICS"u8);
            var metricCount = 0;
            if ((metrics & HotKeysMetrics.Cpu) != 0) metricCount++;
            if ((metrics & HotKeysMetrics.Network) != 0) metricCount++;
            writer.WriteBulkString(metricCount);
            if ((metrics & HotKeysMetrics.Cpu) != 0) writer.WriteBulkString("CPU"u8);
            if ((metrics & HotKeysMetrics.Network) != 0) writer.WriteBulkString("NET"u8);

            if (count != 0)
            {
                writer.WriteBulkString("COUNT"u8);
                writer.WriteBulkString(count);
            }

            if (duration != TimeSpan.Zero)
            {
                writer.WriteBulkString("DURATION"u8);
                writer.WriteBulkString(Math.Ceiling(duration.TotalSeconds));
            }

            if (sampleRatio != 1)
            {
                writer.WriteBulkString("SAMPLE"u8);
                writer.WriteBulkString(sampleRatio);
            }

            if (slots is { Length: > 0 })
            {
                writer.WriteBulkString("SLOTS"u8);
                writer.WriteBulkString(slots.Length);
                foreach (var slot in slots)
                {
                    writer.WriteBulkString(slot);
                }
            }
        }

        public override int ArgCount
        {
            get
            {
                int argCount = 3;
                if ((metrics & HotKeysMetrics.Cpu) != 0) argCount++;
                if ((metrics & HotKeysMetrics.Network) != 0) argCount++;
                if (count != 0) argCount += 2;
                if (duration != TimeSpan.Zero) argCount += 2;
                if (sampleRatio != 1) argCount += 2;
                if (slots is { Length: > 0 }) argCount += 2 + slots.Length;
                return argCount;
            }
        }
    }
}
