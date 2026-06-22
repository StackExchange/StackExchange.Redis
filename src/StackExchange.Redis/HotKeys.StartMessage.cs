using System;

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
            writer.WriteRaw("$5\r\nSTART\r\n"u8);
            writer.WriteRaw("$7\r\nMETRICS\r\n"u8);
            var metricCount = 0;
            if ((metrics & HotKeysMetrics.Cpu) != 0) metricCount++;
            if ((metrics & HotKeysMetrics.Network) != 0) metricCount++;
            writer.WriteBulkString(metricCount);
            if ((metrics & HotKeysMetrics.Cpu) != 0) writer.WriteRaw("$3\r\nCPU\r\n"u8);
            if ((metrics & HotKeysMetrics.Network) != 0) writer.WriteRaw("$3\r\nNET\r\n"u8);

            if (count != 0)
            {
                writer.WriteRaw("$5\r\nCOUNT\r\n"u8);
                writer.WriteBulkString(count);
            }

            if (duration != TimeSpan.Zero)
            {
                writer.WriteRaw("$8\r\nDURATION\r\n"u8);
                writer.WriteBulkString(Math.Ceiling(duration.TotalSeconds));
            }

            if (sampleRatio != 1)
            {
                writer.WriteRaw("$6\r\nSAMPLE\r\n"u8);
                writer.WriteBulkString(sampleRatio);
            }

            if (slots is { Length: > 0 })
            {
                writer.WriteRaw("$5\r\nSLOTS\r\n"u8);
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
