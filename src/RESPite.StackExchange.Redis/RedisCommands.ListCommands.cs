using System.Runtime.CompilerServices;
using RESPite.Messages;
using StackExchange.Redis;

// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable InconsistentNaming
namespace RESPite.StackExchange.Redis;

internal static partial class RedisCommands
{
    // this is just a "type pun" - it should be an invisible/magic pointer cast to the JIT
    public static ref readonly ListCommands Lists(this in RespContext context)
        => ref Unsafe.As<RespContext, ListCommands>(ref Unsafe.AsRef(in context));
}

public readonly struct ListCommands(in RespContext context)
{
    public readonly RespContext Context = context; // important: this is the only field
}

internal static partial class ListCommandsExtensions
{
    /*
    [RespCommand]
    public static partial RespOperation<RedisValue> BLMove(
        this in ListCommands context,
        RedisKey source,
        RedisKey destination,
        ListSide sourceSide,
        ListSide destinationSide,
        double timeoutSeconds);

    [RespCommand]
    public static partial RespOperation<RedisValue[]> BLMPop(
        this in ListCommands context,
        [RespKey] RedisKey[] keys,
        ListSide side,
        long count,
        double timeoutSeconds);

    [RespCommand]
    public static partial RespOperation<RedisValue> BLPop(
        this in ListCommands context,
        [RespKey] RedisKey[] keys,
        double timeoutSeconds);

    [RespCommand]
    public static partial RespOperation<RedisValue> BRPop(
        this in ListCommands context,
        [RespKey] RedisKey[] keys,
        double timeoutSeconds);

    [RespCommand]
    public static partial RespOperation<RedisValue> BRPopLPush(
        this in ListCommands context,
        RedisKey source,
        RedisKey destination,
        double timeoutSeconds);
    */

    [RespCommand]
    public static partial RespOperation<RedisValue> LIndex(this in ListCommands context, RedisKey key, long index);

    [RespCommand(Formatter = "LInsertFormatter.Instance")]
    public static partial RespOperation<long> LInsert(
        this in ListCommands context,
        RedisKey key,
        bool insertBefore,
        RedisValue pivot,
        RedisValue element);

    private sealed class
        LInsertFormatter : IRespFormatter<(RedisKey Key, bool InsertBefore, RedisValue Pivot, RedisValue Element)>
    {
        public static readonly LInsertFormatter Instance = new();
        private LInsertFormatter() { }

        public void Format(
            scoped ReadOnlySpan<byte> command,
            ref RespWriter writer,
            in (RedisKey Key, bool InsertBefore, RedisValue Pivot, RedisValue Element) request)
        {
            writer.WriteCommand(command, 4);
            writer.Write(request.Key);
            writer.WriteRaw(request.InsertBefore ? "$6\r\nBEFORE\r\n"u8 : "$5\r\nAFTER\r\n"u8);
            writer.Write(request.Pivot);
            writer.Write(request.Element);
        }
    }

    [RespCommand]
    public static partial RespOperation<long> LLen(this in ListCommands context, RedisKey key);

    [RespCommand]
    public static partial RespOperation<RedisValue> LMove(
        this in ListCommands context,
        RedisKey source,
        RedisKey destination,
        ListSide sourceSide,
        ListSide destinationSide);

    [RespCommand]
    public static partial RespOperation<RedisValue> LPop(this in ListCommands context, RedisKey key);

    [RespCommand]
    public static partial RespOperation<RedisValue[]> LPop(this in ListCommands context, RedisKey key, long count);

    [RespCommand(Parser = "RespParsers.Int64Index")]
    public static partial RespOperation<long> LPos(
        this in ListCommands context,
        RedisKey key,
        RedisValue element,
        [RespPrefix("RANK"), RespIgnore(1)] long rank = 1,
        [RespPrefix("MAXLEN"), RespIgnore(0)] long maxLen = 0);

    [RespCommand]
    public static partial RespOperation<long[]> LPos(
        this in ListCommands context,
        RedisKey key,
        RedisValue element,
        [RespPrefix("RANK"), RespIgnore(1)] long rank,
        [RespPrefix("MAXLEN"), RespIgnore(0)] long maxLen,
        [RespPrefix("COUNT")] long count);

    [RespCommand]
    public static partial RespOperation<long> LPush(this in ListCommands context, RedisKey key, RedisValue element);

    internal static RespOperation<long> Push(this in ListCommands context, RedisKey key, RedisValue element, ListSide side, When when)
    {
        switch (when)
        {
            case When.Always:
                return side == ListSide.Left ? LPush(context, key, element) : RPush(context, key, element);
            case When.Exists:
                return side == ListSide.Left ? LPushX(context, key, element) : RPushX(context, key, element);
            default:
                when.AlwaysOrExists(); // throws
                return default;
        }
    }

    internal static RespOperation<long> Push(this in ListCommands context, RedisKey key, RedisValue[] elements, ListSide side, When when)
    {
        switch (when)
        {
            case When.Always when elements.Length == 1:
                return side == ListSide.Left ? LPush(context, key, elements[0]) : RPush(context, key, elements[0]);
            case When.Always when elements.Length > 1:
                return side == ListSide.Left ? LPush(context, key, elements) : RPush(context, key, elements);
            case When.Exists when elements.Length == 1:
                return side == ListSide.Left ? LPushX(context, key, elements[0]) : RPushX(context, key, elements[0]);
            case When.Exists when elements.Length > 1:
                return side == ListSide.Left ? LPushX(context, key, elements) : RPushX(context, key, elements);
            default:
                when.AlwaysOrExists(); // check that "when" is valid
                return LLen(context, key); // handle zero case (no insert, just get length)
        }
    }

    [RespCommand]
    public static partial RespOperation<long> LPush(this in ListCommands context, RedisKey key, RedisValue[] elements);

    [RespCommand]
    public static partial RespOperation<long> LPushX(this in ListCommands context, RedisKey key, RedisValue element);

    [RespCommand]
    public static partial RespOperation<long> LPushX(this in ListCommands context, RedisKey key, RedisValue[] elements);

    [RespCommand]
    public static partial RespOperation<RedisValue[]> LRange(
        this in ListCommands context,
        RedisKey key,
        long start,
        long stop);

    [RespCommand]
    public static partial RespOperation<long> LRem(
        this in ListCommands context,
        RedisKey key,
        long count,
        RedisValue element);

    [RespCommand]
    public static partial RespOperation LSet(
        this in ListCommands context,
        RedisKey key,
        long index,
        RedisValue element);

    [RespCommand]
    public static partial RespOperation LTrim(this in ListCommands context, RedisKey key, long start, long stop);

    [RespCommand]
    public static partial RespOperation<RedisValue> RPop(this in ListCommands context, RedisKey key);

    [RespCommand]
    public static partial RespOperation<RedisValue[]> RPop(this in ListCommands context, RedisKey key, long count);

    [RespCommand]
    public static partial RespOperation<RedisValue> RPopLPush(
        this in ListCommands context,
        RedisKey source,
        RedisKey destination);

    [RespCommand]
    public static partial RespOperation<long> RPush(this in ListCommands context, RedisKey key, RedisValue element);

    [RespCommand]
    public static partial RespOperation<long> RPush(this in ListCommands context, RedisKey key, RedisValue[] elements);

    [RespCommand]
    public static partial RespOperation<long> RPushX(this in ListCommands context, RedisKey key, RedisValue element);

    [RespCommand]
    public static partial RespOperation<long> RPushX(this in ListCommands context, RedisKey key, RedisValue[] elements);

    [RespCommand(Parser = "RespParsers.ListPopResult")]
    public static partial RespOperation<ListPopResult> LMPop(
        this in ListCommands context,
        [RespPrefix, RespKey] RedisKey[] keys,
        ListSide side,
        [RespIgnore(1), RespPrefix("COUNT")] long count = 1);
}
