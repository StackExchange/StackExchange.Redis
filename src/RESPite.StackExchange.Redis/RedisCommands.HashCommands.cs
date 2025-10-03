using System.Runtime.CompilerServices;
using RESPite.Messages;
using StackExchange.Redis;

// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable InconsistentNaming
namespace RESPite.StackExchange.Redis;

internal static partial class RedisCommands
{
    // this is just a "type pun" - it should be an invisible/magic pointer cast to the JIT
    public static ref readonly HashCommands Hashes(this in RespContext context)
        => ref Unsafe.As<RespContext, HashCommands>(ref Unsafe.AsRef(in context));
}

public readonly struct HashCommands(in RespContext context)
{
    public readonly RespContext Context = context; // important: this is the only field
}

internal static partial class HashCommandsExtensions
{
    [RespCommand]
    public static partial RespOperation<bool> HDel(this in HashCommands context, RedisKey key, RedisValue hashField);

    [RespCommand]
    public static partial RespOperation<long> HDel(this in HashCommands context, RedisKey key, RedisValue[] hashFields);

    [RespCommand]
    public static partial RespOperation<bool> HExists(this in HashCommands context, RedisKey key, RedisValue hashField);

    [RespCommand]
    public static partial RespOperation<RedisValue> HGet(this in HashCommands context, RedisKey key, RedisValue hashField);

    [RespCommand]
    public static partial RespOperation<HashEntry[]> HGetAll(this in HashCommands context, RedisKey key);

    [RespCommand]
    public static partial RespOperation<long> HIncrBy(this in HashCommands context, RedisKey key, RedisValue hashField, long value = 1);

    [RespCommand]
    public static partial RespOperation<double> HIncrByFloat(this in HashCommands context, RedisKey key, RedisValue hashField, double value);

    [RespCommand]
    public static partial RespOperation<RedisValue[]> HKeys(this in HashCommands context, RedisKey key);

    [RespCommand]
    public static partial RespOperation<long> HLen(this in HashCommands context, RedisKey key);

    [RespCommand]
    public static partial RespOperation<RedisValue[]> HMGet(this in HashCommands context, RedisKey key, RedisValue[] hashFields);

    [RespCommand]
    public static partial RespOperation<RedisValue> HRandField(this in HashCommands context, RedisKey key);

    [RespCommand]
    public static partial RespOperation<RedisValue[]> HRandField(this in HashCommands context, RedisKey key, long count);

    [RespCommand]
    public static partial RespOperation<HashEntry[]> HRandFieldWithValues(this in HashCommands context, RedisKey key, [RespSuffix("WITHVALUES")] long count);

    [RespCommand]
    public static partial RespOperation<bool> HSet(this in HashCommands context, RedisKey key, RedisValue hashField, RedisValue value);

    internal static RespOperation<bool> HSet(
        this in HashCommands context,
        RedisKey key,
        RedisValue hashField,
        RedisValue value,
        When when)
    {
        switch (when)
        {
            case When.Always:
                return HSet(context, key, hashField, value);
            case When.NotExists:
                return HSetNX(context, key, hashField, value);
            default:
                when.AlwaysOrNotExists(); // throws
                return default;
        }
    }

    [RespCommand(Formatter = "HSetFormatter.Instance")]
    public static partial RespOperation HSet(this in HashCommands context, RedisKey key, HashEntry[] hashFields);

    private sealed class HSetFormatter : IRespFormatter<(RedisKey Key, HashEntry[] HashFields)>
    {
        private HSetFormatter() { }
        public static readonly HSetFormatter Instance = new();

        public void Format(
            scoped ReadOnlySpan<byte> command,
            ref RespWriter writer,
            in (RedisKey Key, HashEntry[] HashFields) request)
        {
            writer.WriteCommand(command, 1 + (request.HashFields.Length * 2));
            writer.Write(request.Key);
            foreach (var entry in request.HashFields)
            {
                writer.Write(entry.Name);
                writer.Write(entry.Value);
            }
        }
    }

    [RespCommand]
    public static partial RespOperation<bool> HSetNX(this in HashCommands context, RedisKey key, RedisValue hashField, RedisValue value);

    [RespCommand]
    public static partial RespOperation<long> HStrLen(this in HashCommands context, RedisKey key, RedisValue hashField);

    [RespCommand]
    public static partial RespOperation<RedisValue[]> HVals(this in HashCommands context, RedisKey key);
}
