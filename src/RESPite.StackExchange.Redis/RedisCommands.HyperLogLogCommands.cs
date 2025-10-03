using System.Runtime.CompilerServices;
using StackExchange.Redis;

// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable InconsistentNaming
namespace RESPite.StackExchange.Redis;

internal static partial class RedisCommands
{
    // this is just a "type pun" - it should be an invisible/magic pointer cast to the JIT
    public static ref readonly HyperLogLogCommands HyperLogLogs(this in RespContext context)
        => ref Unsafe.As<RespContext, HyperLogLogCommands>(ref Unsafe.AsRef(in context));
}

public readonly struct HyperLogLogCommands(in RespContext context)
{
    public readonly RespContext Context = context; // important: this is the only field
}

internal static partial class HyperLogLogCommandsExtensions
{
    [RespCommand]
    public static partial RespOperation<bool> PfAdd(this in HyperLogLogCommands context, RedisKey key, RedisValue value);

    [RespCommand]
    public static partial RespOperation<bool> PfAdd(this in HyperLogLogCommands context, RedisKey key, RedisValue[] values);

    [RespCommand]
    public static partial RespOperation<long> PfCount(this in HyperLogLogCommands context, RedisKey key);

    [RespCommand]
    public static partial RespOperation<long> PfCount(this in HyperLogLogCommands context, RedisKey[] keys);

    [RespCommand]
    public static partial RespOperation PfMerge(this in HyperLogLogCommands context, RedisKey destination, RedisKey first, RedisKey second);

    [RespCommand]
    public static partial RespOperation PfMerge(this in HyperLogLogCommands context, RedisKey destination, RedisKey[] sourceKeys);
}
