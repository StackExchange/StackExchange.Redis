using System.Runtime.CompilerServices;
using StackExchange.Redis;

// ReSharper disable InconsistentNaming
// ReSharper disable MemberCanBePrivate.Global
namespace RESPite.StackExchange.Redis;

internal static partial class RedisCommands
{
    // this is just a "type pun" - it should be an invisible/magic pointer cast to the JIT
    public static ref readonly SetCommands Sets(this in RespContext context)
        => ref Unsafe.As<RespContext, SetCommands>(ref Unsafe.AsRef(in context));
}

public readonly struct SetCommands(in RespContext context)
{
    public readonly RespContext Context = context; // important: this is the only field
}

internal static partial class SetCommandsExtensions
{
    [RespCommand]
    public static partial RespOperation<bool> SAdd(this in SetCommands context, RedisKey key, RedisValue member);

    [RespCommand]
    public static partial RespOperation<long> SAdd(this in SetCommands context, RedisKey key, RedisValue[] members);

    [RespCommand]
    public static partial RespOperation<long> SCard(this in SetCommands context, RedisKey key);

    [RespCommand]
    public static partial RespOperation<RedisValue[]> SDiff(this in SetCommands context, RedisKey first, RedisKey second);

    [RespCommand]
    public static partial RespOperation<RedisValue[]> SDiff(this in SetCommands context, RedisKey[] keys);

    [RespCommand]
    public static partial RespOperation<long> SDiffStore(this in SetCommands context, RedisKey destination, RedisKey first, RedisKey second);

    [RespCommand]
    public static partial RespOperation<long> SDiffStore(this in SetCommands context, RedisKey destination, RedisKey[] keys);

    [RespCommand]
    public static partial RespOperation<RedisValue[]> SInter(this in SetCommands context, RedisKey first, RedisKey second);

    [RespCommand]
    public static partial RespOperation<RedisValue[]> SInter(this in SetCommands context, RedisKey[] keys);

    [RespCommand]
    public static partial RespOperation<long> SInterCard(this in SetCommands context, RedisKey first, RedisKey second, long limit = 0);

    [RespCommand]
    public static partial RespOperation<long> SInterCard(this in SetCommands context, RedisKey[] keys, long limit = 0);

    [RespCommand]
    public static partial RespOperation<long> SInterStore(this in SetCommands context, RedisKey destination, RedisKey first, RedisKey second);

    [RespCommand]
    public static partial RespOperation<long> SInterStore(this in SetCommands context, RedisKey destination, RedisKey[] keys);

    [RespCommand]
    public static partial RespOperation<bool> SIsMember(this in SetCommands context, RedisKey key, RedisValue member);

    [RespCommand]
    public static partial RespOperation<bool[]> SMIsMember(this in SetCommands context, RedisKey key, RedisValue[] members);

    [RespCommand]
    public static partial RespOperation<RedisValue[]> SMembers(this in SetCommands context, RedisKey key);

    [RespCommand]
    public static partial RespOperation<bool> SMove(this in SetCommands context, RedisKey source, RedisKey destination, RedisValue member);

    [RespCommand]
    public static partial RespOperation<RedisValue> SPop(this in SetCommands context, RedisKey key);

    [RespCommand]
    public static partial RespOperation<RedisValue[]> SPop(this in SetCommands context, RedisKey key, long count);

    [RespCommand]
    public static partial RespOperation<RedisValue> SRandMember(this in SetCommands context, RedisKey key);

    [RespCommand]
    public static partial RespOperation<RedisValue[]> SRandMember(this in SetCommands context, RedisKey key, long count);

    [RespCommand]
    public static partial RespOperation<bool> SRem(this in SetCommands context, RedisKey key, RedisValue member);

    [RespCommand]
    public static partial RespOperation<long> SRem(this in SetCommands context, RedisKey key, RedisValue[] members);

    [RespCommand]
    public static partial RespOperation<RedisValue[]> SUnion(this in SetCommands context, RedisKey first, RedisKey second);

    [RespCommand]
    public static partial RespOperation<RedisValue[]> SUnion(this in SetCommands context, RedisKey[] keys);

    [RespCommand]
    public static partial RespOperation<long> SUnionStore(this in SetCommands context, RedisKey destination, RedisKey first, RedisKey second);

    [RespCommand]
    public static partial RespOperation<long> SUnionStore(this in SetCommands context, RedisKey destination, RedisKey[] keys);

    internal static RespOperation<long> CombineStore(
        this in SetCommands context,
        SetOperation operation,
        RedisKey destination,
        RedisKey first,
        RedisKey second) =>
        operation switch
        {
            SetOperation.Difference => context.SDiffStore(destination, first, second),
            SetOperation.Intersect => context.SInterStore(destination, first, second),
            SetOperation.Union => context.SUnionStore(destination, first, second),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

    internal static RespOperation<long> CombineStore(
        this in SetCommands context,
        SetOperation operation,
        RedisKey destination,
        RedisKey[] keys) =>
        operation switch
        {
            SetOperation.Difference => context.SDiffStore(destination, keys),
            SetOperation.Intersect => context.SInterStore(destination, keys),
            SetOperation.Union => context.SUnionStore(destination, keys),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

    internal static RespOperation<RedisValue[]> Combine(
        this in SetCommands context,
        SetOperation operation,
        RedisKey first,
        RedisKey second) =>
        operation switch
        {
            SetOperation.Difference => context.SDiff(first, second),
            SetOperation.Intersect => context.SInter(first, second),
            SetOperation.Union => context.SUnion(first, second),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

    internal static RespOperation<RedisValue[]> Combine(
        this in SetCommands context,
        SetOperation operation,
        RedisKey[] keys) =>
        operation switch
        {
            SetOperation.Difference => context.SDiff(keys),
            SetOperation.Intersect => context.SInter(keys),
            SetOperation.Union => context.SUnion(keys),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };
}
