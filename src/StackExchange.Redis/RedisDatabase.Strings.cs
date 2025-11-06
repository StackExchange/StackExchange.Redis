using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace StackExchange.Redis;

internal partial class RedisDatabase
{
    public bool StringDelete(RedisKey key, ValueCondition when, CommandFlags flags = CommandFlags.None)
    {
        var msg = GetStringDeleteMessage(key, when, flags);
        return ExecuteSync(msg, ResultProcessor.Boolean);
    }

    public Task<bool> StringDeleteAsync(RedisKey key, ValueCondition when, CommandFlags flags = CommandFlags.None)
    {
        var msg = GetStringDeleteMessage(key, when, flags);
        return ExecuteAsync(msg, ResultProcessor.Boolean);
    }

    private Message GetStringDeleteMessage(in RedisKey key, in ValueCondition when, CommandFlags flags, [CallerMemberName] string? operation = null)
    {
        switch (when.Kind)
        {
            case ValueCondition.ConditionKind.Always:
            case ValueCondition.ConditionKind.Exists:
                return Message.Create(Database, flags, RedisCommand.DEL, key);
            case ValueCondition.ConditionKind.ValueEquals:
            case ValueCondition.ConditionKind.ValueNotEquals:
            case ValueCondition.ConditionKind.DigestEquals:
            case ValueCondition.ConditionKind.DigestNotEquals:
                return Message.Create(Database, flags, RedisCommand.DELEX, key, when);
            default:
                when.ThrowInvalidOperation(operation);
                goto case ValueCondition.ConditionKind.Always; // not reached
        }
    }

    public ValueCondition? StringDigest(RedisKey key, CommandFlags flags = CommandFlags.None)
    {
        var msg = Message.Create(Database, flags, RedisCommand.DIGEST, key);
        return ExecuteSync(msg, ResultProcessor.Digest);
    }

    public Task<ValueCondition?> StringDigestAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
    {
        var msg = Message.Create(Database, flags, RedisCommand.DIGEST, key);
        return ExecuteAsync(msg, ResultProcessor.Digest);
    }

    public Task<bool> StringSetAsync(RedisKey key, RedisValue value, Expiration expiry, ValueCondition when, CommandFlags flags = CommandFlags.None)
    {
        var msg = GetStringSetMessage(key, value, expiry, when, flags);
        return ExecuteAsync(msg, ResultProcessor.Boolean);
    }

    public bool StringSet(RedisKey key, RedisValue value, Expiration expiry, ValueCondition when, CommandFlags flags = CommandFlags.None)
    {
        var msg = GetStringSetMessage(key, value, expiry, when, flags);
        return ExecuteSync(msg, ResultProcessor.Boolean);
    }

    private Message GetStringSetMessage(in RedisKey key, in RedisValue value, Expiration expiry, in ValueCondition when, CommandFlags flags, [CallerMemberName] string? operation = null)
    {
        switch (when.Kind)
        {
            case ValueCondition.ConditionKind.Exists:
            case ValueCondition.ConditionKind.NotExists:
            case ValueCondition.ConditionKind.Always:
                return GetStringSetMessage(key, value, expiry: expiry, when: when.AsWhen(), flags: flags);
            case ValueCondition.ConditionKind.ValueEquals:
            case ValueCondition.ConditionKind.ValueNotEquals:
            case ValueCondition.ConditionKind.DigestEquals:
            case ValueCondition.ConditionKind.DigestNotEquals:
                return Message.Create(Database, flags, RedisCommand.SET, key, value, expiry, when);
            default:
                when.ThrowInvalidOperation(operation);
                goto case ValueCondition.ConditionKind.Always; // not reached
        }
    }
}
