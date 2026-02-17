using System;

namespace StackExchange.Redis;

internal partial class Message
{
    public static Message Create(int db, CommandFlags flags, RedisCommand command, in RedisKey key, in ValueCondition when)
        => new KeyConditionMessage(db, flags, command, key, when);

    public static Message Create(int db, CommandFlags flags, RedisCommand command, in RedisKey key, in RedisValue value, Expiration expiry, in ValueCondition when)
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

        protected override void WriteImpl(in MessageWriter writer)
        {
            writer.WriteHeader(Command, ArgCount);
            writer.Write(Key);
            _when.WriteTo(writer);
        }
    }

    private sealed class KeyValueExpiryConditionMessage(
        int db,
        CommandFlags flags,
        RedisCommand command,
        in RedisKey key,
        in RedisValue value,
        Expiration expiry,
        in ValueCondition when)
        : CommandKeyBase(db, flags, command, key)
    {
        private readonly RedisValue _value = value;
        private readonly ValueCondition _when = when;
        private readonly Expiration _expiry = expiry;

        public override int ArgCount => 2 + _expiry.TokenCount + _when.TokenCount;

        protected override void WriteImpl(in MessageWriter writer)
        {
            writer.WriteHeader(Command, ArgCount);
            writer.Write(Key);
            writer.WriteBulkString(_value);
            _expiry.WriteTo(writer);
            _when.WriteTo(writer);
        }
    }
}
