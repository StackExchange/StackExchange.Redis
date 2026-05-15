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

    public StringIncrementResult<long> StringIncrement(RedisKey key, long value, Expiration expiry, long? lowerBound = null, long? upperBound = null, IncrementOverflow overflow = IncrementOverflow.Fail, CommandFlags flags = CommandFlags.None)
    {
        ValidateStringIncrementExpiry(expiry);
        ValidateIncrementOverflow(overflow);
        var msg = new IncrexInt64Message(Database, flags, key, value, lowerBound, upperBound, expiry, overflow);
        return ExecuteSync(msg, IncrexResultProcessor.Int64);
    }

    public Task<StringIncrementResult<long>> StringIncrementAsync(RedisKey key, long value, Expiration expiry, long? lowerBound = null, long? upperBound = null, IncrementOverflow overflow = IncrementOverflow.Fail, CommandFlags flags = CommandFlags.None)
    {
        ValidateStringIncrementExpiry(expiry);
        ValidateIncrementOverflow(overflow);
        var msg = new IncrexInt64Message(Database, flags, key, value, lowerBound, upperBound, expiry, overflow);
        return ExecuteAsync(msg, IncrexResultProcessor.Int64);
    }

    public StringIncrementResult<double> StringIncrement(RedisKey key, double value, Expiration expiry, double? lowerBound = null, double? upperBound = null, IncrementOverflow overflow = IncrementOverflow.Fail, CommandFlags flags = CommandFlags.None)
    {
        ValidateStringIncrementExpiry(expiry);
        ValidateIncrementOverflow(overflow);
        var msg = new IncrexDoubleMessage(Database, flags, key, value, lowerBound, upperBound, expiry, overflow);
        return ExecuteSync(msg, IncrexResultProcessor.Double);
    }

    public Task<StringIncrementResult<double>> StringIncrementAsync(RedisKey key, double value, Expiration expiry, double? lowerBound = null, double? upperBound = null, IncrementOverflow overflow = IncrementOverflow.Fail, CommandFlags flags = CommandFlags.None)
    {
        ValidateStringIncrementExpiry(expiry);
        ValidateIncrementOverflow(overflow);
        var msg = new IncrexDoubleMessage(Database, flags, key, value, lowerBound, upperBound, expiry, overflow);
        return ExecuteAsync(msg, IncrexResultProcessor.Double);
    }

    private static void ValidateStringIncrementExpiry(Expiration expiry)
    {
        if (expiry.IsKeepTtl) ThrowKeepTtl();
        if (expiry.IsPersist) ThrowPersist();
        if (expiry.IsExpireIfNotExists && !(expiry.IsAbsolute || expiry.IsRelative)) ThrowEnxWithoutExpiry();

        static void ThrowKeepTtl() => throw new ArgumentException("KEEPTTL is not supported by this operation.", nameof(expiry));
        static void ThrowPersist() => throw new ArgumentException("PERSIST is not supported by this operation.", nameof(expiry));
        static void ThrowEnxWithoutExpiry() => throw new ArgumentException("ENX requires EX, PX, EXAT, or PXAT.", nameof(expiry));
    }

    private static void ValidateIncrementOverflow(IncrementOverflow overflow)
    {
        switch (overflow)
        {
            case IncrementOverflow.Fail:
            case IncrementOverflow.Reject:
            case IncrementOverflow.Saturate:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(overflow));
        }
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
