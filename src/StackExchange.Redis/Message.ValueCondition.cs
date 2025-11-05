using System;

namespace StackExchange.Redis;

internal partial class Message
{
    public static Message Create(int db, CommandFlags flags, RedisCommand command, in RedisKey key, in ValueCondition when)
        => new KeyConditionMessage(db, flags, command, key, when);

    public static Message Create(int db, CommandFlags flags, RedisCommand command, in RedisKey key, in RedisValue value, TimeSpan? expiry, in ValueCondition when)
        => new KeyValueExpiryConditionMessage(db, flags, command, key, value, expiry, when);

    private sealed class KeyConditionMessage(
        int db,
        CommandFlags flags,
        RedisCommand command,
        in RedisKey key,
        in ValueCondition when)
        : CommandKeyBase(db, flags, command, key)
    {
        private readonly ValueCondition _when = when;

        public override int ArgCount => 1 + _when.TokenCount;

        protected override void WriteImpl(PhysicalConnection physical)
        {
            physical.WriteHeader(Command, ArgCount);
            physical.Write(Key);
            _when.WriteTo(physical);
        }
    }

    private sealed class KeyValueExpiryConditionMessage(
        int db,
        CommandFlags flags,
        RedisCommand command,
        in RedisKey key,
        in RedisValue value,
        TimeSpan? expiry,
        in ValueCondition when)
        : CommandKeyBase(db, flags, command, key)
    {
        private readonly RedisValue _value = value;
        private readonly ValueCondition _when = when;
        private readonly TimeSpan? _expiry = expiry == TimeSpan.MaxValue ? null : expiry;

        public override int ArgCount => 2 + _when.TokenCount + (_expiry is null ? 0 : 2);

        protected override void WriteImpl(PhysicalConnection physical)
        {
            physical.WriteHeader(Command, ArgCount);
            physical.Write(Key);
            physical.WriteBulkString(_value);
            if (_expiry.HasValue)
            {
                var ms = (long)_expiry.GetValueOrDefault().TotalMilliseconds;
                if ((ms % 1000) == 0)
                {
                    physical.WriteBulkString("EX"u8);
                    physical.WriteBulkString(ms / 1000);
                }
                else
                {
                    physical.WriteBulkString("PX"u8);
                    physical.WriteBulkString(ms);
                }
            }
            _when.WriteTo(physical);
        }
    }
}
