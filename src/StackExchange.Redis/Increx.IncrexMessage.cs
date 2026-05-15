using System;

namespace StackExchange.Redis;

internal partial class RedisDatabase
{
    internal abstract class IncrexMessageBase(
        int database,
        CommandFlags flags,
        RedisKey key,
        Expiration expiry,
        IncrementOverflow overflow) : Message(database, flags, RedisCommand.INCREX)
    {
        protected RedisKey Key => key;
        protected Expiration Expiry => expiry;
        protected IncrementOverflow Overflow => overflow;

        public override int ArgCount
        {
            get
            {
                return 3 + BoundsArgCount + OverflowArgCount + Expiry.GetTokenCount(allowEnx: true); // key, BYINT/BYFLOAT, value, bounds, overflow, expiry
            }
        }

        private int OverflowArgCount => Overflow == IncrementOverflow.Fail ? 0 : 2;

        protected abstract int BoundsArgCount { get; }
        protected abstract void WriteIncrementKindAndValue(PhysicalConnection physical);
        protected abstract void WriteBounds(PhysicalConnection physical);

        protected override void WriteImpl(PhysicalConnection physical)
        {
            physical.WriteHeader(Command, ArgCount);
            physical.WriteBulkString(Key);
            WriteIncrementKindAndValue(physical);
            WriteBounds(physical);
            WriteOverflow(physical);
            Expiry.WriteTo(physical);
        }

        private void WriteOverflow(PhysicalConnection physical)
        {
            switch (Overflow)
            {
                case IncrementOverflow.Fail:
                    break;
                case IncrementOverflow.Reject:
                    physical.WriteRaw("$8\r\nOVERFLOW\r\n$6\r\nREJECT\r\n"u8);
                    break;
                case IncrementOverflow.Saturate:
                    physical.WriteRaw("$8\r\nOVERFLOW\r\n$3\r\nSAT\r\n"u8);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(Overflow));
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
        IncrementOverflow overflow) : IncrexMessageBase(database, flags, key, expiry, overflow)
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
        IncrementOverflow overflow) : IncrexMessageBase(database, flags, key, expiry, overflow)
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
