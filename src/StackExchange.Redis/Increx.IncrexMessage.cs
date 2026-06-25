using System;

namespace StackExchange.Redis;

internal partial class RedisDatabase
{
    internal abstract class IncrexMessageBase(
        int database,
        CommandFlags flags,
        RedisKey key,
        Expiration expiry,
        IncrementOptions options) : Message(database, flags, RedisCommand.INCREX)
    {
        protected RedisKey Key => key;
        protected Expiration Expiry => expiry;
        protected IncrementOptions Options => options;

        public override int ArgCount
        {
            get
            {
                return 3 + BoundsArgCount + OptionsArgCount + Expiry.GetTokenCount(allowEnx: true); // key, BYINT/BYFLOAT, value, bounds, options, expiry
            }
        }

        private int OptionsArgCount => Options == IncrementOptions.Saturate ? 1 : 0;

        protected abstract int BoundsArgCount { get; }
        protected abstract void WriteIncrementKindAndValue(in MessageWriter writer);
        protected abstract void WriteBounds(in MessageWriter writer);

        protected override void WriteImpl(in MessageWriter writer)
        {
            writer.WriteHeader(Command, ArgCount);
            writer.WriteBulkString(Key);
            WriteIncrementKindAndValue(writer);
            WriteBounds(writer);
            WriteOptions(writer);
            Expiry.WriteTo(writer);
        }

        private void WriteOptions(in MessageWriter writer)
        {
            switch (Options)
            {
                case IncrementOptions.None:
                    break;
                case IncrementOptions.Saturate:
                    writer.WriteRaw("$8\r\nSATURATE\r\n"u8);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(Options));
            }
        }
    }

    internal sealed class IncrexInt64Message(
        int database,
        CommandFlags flags,
        RedisKey key,
        long value,
        long? lowerBound,
        long? upperBound,
        Expiration expiry,
        IncrementOptions options) : IncrexMessageBase(database, flags, key, expiry, options)
    {
        protected override int BoundsArgCount => (lowerBound.HasValue ? 2 : 0) + (upperBound.HasValue ? 2 : 0);

        protected override void WriteIncrementKindAndValue(in MessageWriter writer)
        {
            writer.WriteRaw("$5\r\nBYINT\r\n"u8);
            writer.WriteBulkString(value);
        }

        protected override void WriteBounds(in MessageWriter writer)
        {
            if (lowerBound.HasValue)
            {
                writer.WriteRaw("$6\r\nLBOUND\r\n"u8);
                writer.WriteBulkString(lowerBound.GetValueOrDefault());
            }
            if (upperBound.HasValue)
            {
                writer.WriteRaw("$6\r\nUBOUND\r\n"u8);
                writer.WriteBulkString(upperBound.GetValueOrDefault());
            }
        }
    }

    internal sealed class IncrexDoubleMessage(
        int database,
        CommandFlags flags,
        RedisKey key,
        double value,
        double? lowerBound,
        double? upperBound,
        Expiration expiry,
        IncrementOptions options) : IncrexMessageBase(database, flags, key, expiry, options)
    {
        protected override int BoundsArgCount => (lowerBound.HasValue ? 2 : 0) + (upperBound.HasValue ? 2 : 0);

        protected override void WriteIncrementKindAndValue(in MessageWriter writer)
        {
            writer.WriteRaw("$7\r\nBYFLOAT\r\n"u8);
            writer.WriteBulkString(value);
        }

        protected override void WriteBounds(in MessageWriter writer)
        {
            if (lowerBound.HasValue)
            {
                writer.WriteRaw("$6\r\nLBOUND\r\n"u8);
                writer.WriteBulkString(lowerBound.GetValueOrDefault());
            }
            if (upperBound.HasValue)
            {
                writer.WriteRaw("$6\r\nUBOUND\r\n"u8);
                writer.WriteBulkString(upperBound.GetValueOrDefault());
            }
        }
    }
}
