namespace StackExchange.Redis;

internal partial class RedisDatabase
{
    internal abstract class IncrexMessageBase(
        int database,
        CommandFlags flags,
        RedisKey key,
        Expiration expiry) : Message(database, flags, RedisCommand.INCREX)
    {
        protected RedisKey Key => key;
        protected Expiration Expiry => expiry;

        protected int GetArgCount(int coreArgs) => 1 + coreArgs + Expiry.GetTokenCount(allowEnx: true);
    }

    internal sealed class IncrexInt64Message(
        int database,
        CommandFlags flags,
        RedisKey key,
        long value,
        long? lowerBound,
        long? upperBound,
        Expiration expiry) : IncrexMessageBase(database, flags, key, expiry)
    {
        public override int ArgCount
        {
            get
            {
                int coreArgs = 2; // BYINT value
                if (lowerBound.HasValue) coreArgs += 2;
                if (upperBound.HasValue) coreArgs += 2;
                return GetArgCount(coreArgs);
            }
        }

        protected override void WriteImpl(PhysicalConnection physical)
        {
            physical.WriteHeader(Command, ArgCount);
            physical.WriteBulkString(Key);
            physical.WriteBulkString("BYINT"u8);
            physical.WriteBulkString(value);
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
            Expiry.WriteTo(physical);
        }
    }

    internal sealed class IncrexDoubleMessage(
        int database,
        CommandFlags flags,
        RedisKey key,
        double value,
        double? lowerBound,
        double? upperBound,
        Expiration expiry) : IncrexMessageBase(database, flags, key, expiry)
    {
        public override int ArgCount
        {
            get
            {
                int coreArgs = 2; // BYFLOAT value
                if (lowerBound.HasValue) coreArgs += 2;
                if (upperBound.HasValue) coreArgs += 2;
                return GetArgCount(coreArgs);
            }
        }

        protected override void WriteImpl(PhysicalConnection physical)
        {
            physical.WriteHeader(Command, ArgCount);
            physical.WriteBulkString(Key);
            physical.WriteBulkString("BYFLOAT"u8);
            physical.WriteBulkString(value);
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
            Expiry.WriteTo(physical);
        }
    }
}
