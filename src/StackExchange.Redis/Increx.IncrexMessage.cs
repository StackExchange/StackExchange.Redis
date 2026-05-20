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
        protected abstract void WriteIncrementKindAndValue(PhysicalConnection physical);
        protected abstract void WriteBounds(PhysicalConnection physical);

        protected override void WriteImpl(PhysicalConnection physical)
        {
            physical.WriteHeader(Command, ArgCount);
            physical.WriteBulkString(Key);
            WriteIncrementKindAndValue(physical);
            WriteBounds(physical);
            WriteOptions(physical);
            Expiry.WriteTo(physical);
        }

        private void WriteOptions(PhysicalConnection physical)
        {
            switch (Options)
            {
                case IncrementOptions.None:
                    break;
                case IncrementOptions.Saturate:
                    physical.WriteRaw("$8\r\nSATURATE\r\n"u8);
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

        protected override void WriteIncrementKindAndValue(PhysicalConnection physical)
        {
            physical.WriteBulkString("BYINT"u8);
            physical.WriteBulkString(value);
        }

        protected override void WriteBounds(PhysicalConnection physical)
        {
            if (lowerBound.HasValue)
            {
                physical.WriteBulkString("LBOUND"u8);
                physical.WriteBulkString(lowerBound.GetValueOrDefault());
            }
            if (upperBound.HasValue)
            {
                physical.WriteBulkString("UBOUND"u8);
                physical.WriteBulkString(upperBound.GetValueOrDefault());
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

        protected override void WriteIncrementKindAndValue(PhysicalConnection physical)
        {
            physical.WriteBulkString("BYFLOAT"u8);
            physical.WriteBulkString(value);
        }

        protected override void WriteBounds(PhysicalConnection physical)
        {
            if (lowerBound.HasValue)
            {
                physical.WriteBulkString("LBOUND"u8);
                physical.WriteBulkString(lowerBound.GetValueOrDefault());
            }
            if (upperBound.HasValue)
            {
                physical.WriteBulkString("UBOUND"u8);
                physical.WriteBulkString(upperBound.GetValueOrDefault());
            }
        }
    }
}
