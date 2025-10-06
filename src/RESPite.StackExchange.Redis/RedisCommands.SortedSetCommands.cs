using System.Runtime.CompilerServices;
using RESPite.Messages;
using StackExchange.Redis;

// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable InconsistentNaming
namespace RESPite.StackExchange.Redis;

internal static partial class RedisCommands
{
    // this is just a "type pun" - it should be an invisible/magic pointer cast to the JIT
    public static ref readonly SortedSetCommands SortedSets(this in RespContext context)
        => ref Unsafe.As<RespContext, SortedSetCommands>(ref Unsafe.AsRef(in context));
}

public readonly struct SortedSetCommands(in RespContext context)
{
    public readonly RespContext Context = context; // important: this is the only field
}

internal static partial class SortedSetCommandsExtensions
{
    [RespCommand]
    public static partial RespOperation<bool> ZAdd(
        this in SortedSetCommands context,
        RedisKey key,
        RedisValue member,
        double score);

    [RespCommand(Formatter = "ZAddFormatter.Instance")]
    public static partial RespOperation<bool> ZAdd(
        this in SortedSetCommands context,
        RedisKey key,
        SortedSetWhen when,
        RedisValue member,
        double score);

    [RespCommand(Formatter = "ZAddFormatter.Instance")]
    public static RespOperation<long> ZAdd(
        this in SortedSetCommands context,
        RedisKey key,
        SortedSetEntry[] values) =>
        context.ZAdd(key, SortedSetWhen.Always, values);

    [RespCommand(Formatter = "ZAddFormatter.Instance")]
    public static partial RespOperation<long> ZAdd(
        this in SortedSetCommands context,
        RedisKey key,
        SortedSetWhen when,
        SortedSetEntry[] values);

    private sealed class ZAddFormatter :
        IRespFormatter<(RedisKey Key, SortedSetWhen When, RedisValue Member, double Score)>,
        IRespFormatter<(RedisKey Key, SortedSetWhen When, SortedSetEntry[] Values)>
    {
        private ZAddFormatter() { }
        public static readonly ZAddFormatter Instance = new();

        public void Format(
            scoped ReadOnlySpan<byte> command,
            ref RespWriter writer,
            in (RedisKey Key, SortedSetWhen When, RedisValue Member, double Score) request)
        {
            var argCount = 3 + GetWhenFlagCount(request.When);
            writer.WriteCommand(command, argCount);
            writer.Write(request.Key);
            WriteWhenFlags(ref writer, request.When);
            writer.WriteBulkString(request.Score);
            writer.Write(request.Member);
        }

        public void Format(
            scoped ReadOnlySpan<byte> command,
            ref RespWriter writer,
            in (RedisKey Key, SortedSetWhen When, SortedSetEntry[] Values) request)
        {
            var argCount = 1 + GetWhenFlagCount(request.When) + (request.Values.Length * 2);
            writer.WriteCommand(command, argCount);
            writer.Write(request.Key);
            WriteWhenFlags(ref writer, request.When);
            foreach (var entry in request.Values)
            {
                writer.WriteBulkString(entry.Score);
                writer.Write(entry.Element);
            }
        }

        private static int GetWhenFlagCount(SortedSetWhen when)
        {
            when &= SortedSetWhen.NotExists | SortedSetWhen.Exists | SortedSetWhen.GreaterThan | SortedSetWhen.LessThan;
            return (int)when.CountBits();
        }

        private static void WriteWhenFlags(ref RespWriter writer, SortedSetWhen when)
        {
            if ((when & SortedSetWhen.NotExists) != 0)
                writer.WriteBulkString("NX"u8);
            if ((when & SortedSetWhen.Exists) != 0)
                writer.WriteBulkString("XX"u8);
            if ((when & SortedSetWhen.GreaterThan) != 0)
                writer.WriteBulkString("GT"u8);
            if ((when & SortedSetWhen.LessThan) != 0)
                writer.WriteBulkString("LT"u8);
        }
    }

    internal static RespOperation<long> ZCardOrCount(
        this in SortedSetCommands context,
        RedisKey key,
        double min,
        double max,
        Exclude exclude)
    {
        if (double.IsNegativeInfinity(min) && double.IsPositiveInfinity(max))
        {
            return context.ZCard(key);
        }

        return context.ZCount(key, exclude.Start(min), exclude.Stop(max));
    }

    [RespCommand]
    public static partial RespOperation<long> ZCard(this in SortedSetCommands context, RedisKey key);

    [RespCommand]
    public static partial RespOperation<long> ZCount(this in SortedSetCommands context, RedisKey key, BoundedDouble min, BoundedDouble max);

    [RespCommand]
    public static partial RespOperation<RedisValue[]> ZDiff(
        this in SortedSetCommands context,
        RedisKey[] keys);

    [RespCommand]
    public static partial RespOperation<RedisValue[]> ZDiff(
        this in SortedSetCommands context,
        RedisKey first,
        RedisKey second);

    [RespCommand(nameof(ZDiff))]
    public static partial RespOperation<SortedSetEntry[]> ZDiffWithScores(
        this in SortedSetCommands context,
        [RespSuffix("WITHSCORES")] RedisKey[] keys);

    [RespCommand(nameof(ZDiff))]
    public static partial RespOperation<SortedSetEntry[]> ZDiffWithScores(
        this in SortedSetCommands context,
        RedisKey first,
        [RespSuffix("WITHSCORES")] RedisKey second);

    [RespCommand]
    public static partial RespOperation<long> ZDiffStore(
        this in SortedSetCommands context,
        RedisKey destination,
        RedisKey[] keys);

    [RespCommand]
    public static partial RespOperation<long> ZDiffStore(
        this in SortedSetCommands context,
        RedisKey destination,
        RedisKey first,
        RedisKey second);

    [RespCommand]
    public static partial RespOperation<double> ZIncrBy(
        this in SortedSetCommands context,
        RedisKey key,
        RedisValue member,
        double increment);

    [RespCommand]
    public static partial RespOperation<RedisValue[]> ZInter(
        this in SortedSetCommands context,
        RedisKey[] keys,
        [RespPrefix("WEIGHTS")] double[]? weights = null,
        [RespPrefix("AGGREGATE")] Aggregate? aggregate = null);

    [RespCommand]
    public static partial RespOperation<RedisValue[]> ZInter(
        this in SortedSetCommands context,
        RedisKey first,
        RedisKey second,
        [RespPrefix("AGGREGATE")] Aggregate? aggregate = null);

    [RespCommand]
    public static partial RespOperation<long> ZInterCard(
        this in SortedSetCommands context,
        [RespPrefix] RedisKey[] keys,
        [RespPrefix("LIMIT"), RespIgnore(0)] long limit = 0);

    [RespCommand]
    public static partial RespOperation<long> ZInterCard(
        this in SortedSetCommands context,
        [RespPrefix("2")] RedisKey first,
        RedisKey second,
        [RespPrefix("LIMIT"), RespIgnore(0)] long limit = 0);

    [RespCommand(nameof(ZInter))]
    public static partial RespOperation<SortedSetEntry[]> ZInterWithScores(
        this in SortedSetCommands context,
        [RespSuffix("WITHSCORES")] RedisKey[] keys,
        [RespPrefix("WEIGHTS")] double[]? weights = null,
        [RespPrefix("AGGREGATE")] Aggregate? aggregate = null);

    [RespCommand(nameof(ZInter))]
    public static partial RespOperation<SortedSetEntry[]> ZInterWithScores(
        this in SortedSetCommands context,
        RedisKey first,
        [RespSuffix("WITHSCORES")] RedisKey second,
        [RespPrefix("AGGREGATE")] Aggregate? aggregate = null);

    [RespCommand]
    public static partial RespOperation<long> ZInterStore(
        this in SortedSetCommands context,
        RedisKey destination,
        RedisKey[] keys,
        [RespPrefix("WEIGHTS")] double[]? weights = null,
        [RespPrefix("AGGREGATE")] Aggregate? aggregate = null);

    [RespCommand]
    public static partial RespOperation<long> ZInterStore(
        this in SortedSetCommands context,
        RedisKey destination,
        RedisKey first,
        RedisKey second,
        [RespPrefix("AGGREGATE")] Aggregate? aggregate = null);

    [RespCommand]
    public static partial RespOperation<long> ZLexCount(
        this in SortedSetCommands context,
        RedisKey key,
        BoundedRedisValue min,
        BoundedRedisValue max);

    internal static RespOperation<long> ZLexCount(
        this in SortedSetCommands context,
        RedisKey key,
        RedisValue min,
        RedisValue max,
        Exclude exclude) => context.ZLexCount(
        key,
        min.IsNull ? BoundedRedisValue.MinValue : exclude.StartLex(min),
        max.IsNull ? BoundedRedisValue.MaxValue : exclude.StopLex(max));

    [RespCommand]
    public static partial RespOperation<SortedSetEntry?> ZPopMax(this in SortedSetCommands context, RedisKey key);

    [RespCommand]
    public static partial RespOperation<SortedSetEntry[]> ZPopMax(
        this in SortedSetCommands context,
        RedisKey key,
        long count);

    [RespCommand]
    public static partial RespOperation<SortedSetEntry?> ZPopMin(this in SortedSetCommands context, RedisKey key);

    [RespCommand]
    public static partial RespOperation<SortedSetEntry[]> ZPopMin(
        this in SortedSetCommands context,
        RedisKey key,
        long count);

    [RespCommand]
    public static partial RespOperation<RedisValue> ZRandMember(this in SortedSetCommands context, RedisKey key);

    [RespCommand]
    public static partial RespOperation<RedisValue[]> ZRandMember(
        this in SortedSetCommands context,
        RedisKey key,
        long count);

    [RespCommand(nameof(ZRandMember))]
    public static partial RespOperation<SortedSetEntry[]> ZRandMemberWithScores(
        this in SortedSetCommands context,
        RedisKey key,
        [RespSuffix("WITHSCORES")] long count);

    [RespCommand(Formatter = "ZRangeFormatter.NoScores")]
    public static partial RespOperation<RedisValue[]> ZRange(
        this in SortedSetCommands context,
        RedisKey key,
        long start,
        long stop,
        Order order = Order.Ascending,
        long offset = 0,
        long count = long.MaxValue);

    [RespCommand(Formatter = "ZRangeFormatter.WithScores")]
    public static partial RespOperation<SortedSetEntry[]> ZRangeWithScores(
        this in SortedSetCommands context,
        RedisKey key,
        long start,
        long stop,
        Order order = Order.Ascending,
        long offset = 0,
        long count = long.MaxValue);

    [RespCommand(Formatter = "ZRangeFormatter.NoScores")] // by lex
    internal static partial RespOperation<RedisValue[]> ZRange(
        this in SortedSetCommands context,
        RedisKey key,
        BoundedRedisValue start,
        BoundedRedisValue stop,
        Order order = Order.Ascending,
        long offset = 0,
        long count = long.MaxValue);

    [RespCommand(Formatter = "ZRangeFormatter.WithScores")] // by lex
    internal static partial RespOperation<SortedSetEntry[]> ZRangeWithScores(
        this in SortedSetCommands context,
        RedisKey key,
        BoundedRedisValue start,
        BoundedRedisValue stop,
        Order order = Order.Ascending,
        long offset = 0,
        long count = long.MaxValue);

    [RespCommand(Formatter = "ZRangeFormatter.NoScores")] // by score
    internal static partial RespOperation<RedisValue[]> ZRange(
        this in SortedSetCommands context,
        RedisKey key,
        BoundedDouble start,
        BoundedDouble stop,
        Order order = Order.Ascending,
        long offset = 0,
        long count = long.MaxValue);

    [RespCommand(Formatter = "ZRangeFormatter.WithScores")] // byscore
    internal static partial RespOperation<SortedSetEntry[]> ZRangeWithScores(
        this in SortedSetCommands context,
        RedisKey key,
        BoundedDouble start,
        BoundedDouble stop,
        Order order = Order.Ascending,
        long offset = 0,
        long count = long.MaxValue);

    private sealed class ZRangeFormatter :
        IRespFormatter<(RedisKey Key, long Start, long Stop, Order Order, long Offset, long Count)>,
        IRespFormatter<(RedisKey Key, BoundedDouble Start, BoundedDouble Stop, Order Order, long Offset, long Count)>,
        IRespFormatter<(RedisKey Key, BoundedRedisValue Start, BoundedRedisValue Stop, Order Order, long Offset, long Count)>
    {
         private readonly bool _withScores;
         private ZRangeFormatter(bool withScores) => _withScores = withScores;
         public static readonly ZRangeFormatter WithScores = new(true), NoScores = new(false);
         public void Format(
            scoped ReadOnlySpan<byte> command,
            ref RespWriter writer,
            in (RedisKey Key, long Start, long Stop, Order Order, long Offset, long Count) request)
         {
            bool writeLimit = request.Offset != 0 || request.Count != long.MaxValue;
            var argCount = 3 + (writeLimit ? 3 : 0) + (_withScores ? 1 : 0) + (request.Order == Order.Descending ? 1 : 0);
            writer.WriteCommand(command, argCount);
            writer.Write(request.Key);
            writer.WriteBulkString(request.Start);
            writer.WriteBulkString(request.Stop);
            if (request.Order == Order.Descending)
            {
                writer.WriteRaw("$3\r\nREV\r\n"u8);
            }
            if (writeLimit)
            {
                writer.WriteRaw("$5\r\nLIMIT\r\n"u8);
                writer.WriteBulkString(request.Offset);
                writer.WriteBulkString(request.Count);
            }
            if (_withScores)
            {
                writer.WriteRaw("$10\r\nWITHSCORES\r\n"u8);
            }
         }

         public void Format(
             scoped ReadOnlySpan<byte> command,
             ref RespWriter writer,
             in (RedisKey Key, BoundedDouble Start, BoundedDouble Stop, Order Order, long Offset, long Count) request)
         {
             bool writeLimit = request.Offset != 0 || request.Count != long.MaxValue;
             var argCount = 4 + (writeLimit ? 3 : 0) + (_withScores ? 1 : 0) + (request.Order == Order.Descending ? 1 : 0);
             writer.WriteCommand(command, argCount);
             writer.Write(request.Key);
             writer.WriteBulkString(request.Start);
             writer.WriteBulkString(request.Stop);
             writer.WriteRaw("$7\r\nBYSCORE\r\n"u8);
             if (request.Order == Order.Descending)
             {
                 writer.WriteRaw("$3\r\nREV\r\n"u8);
             }
             if (writeLimit)
             {
                 writer.WriteRaw("$5\r\nLIMIT\r\n"u8);
                 writer.WriteBulkString(request.Offset);
                 writer.WriteBulkString(request.Count);
             }
             if (_withScores)
             {
                 writer.WriteRaw("$10\r\nWITHSCORES\r\n"u8);
             }
         }

         public void Format(
             scoped ReadOnlySpan<byte> command,
             ref RespWriter writer,
             in (RedisKey Key, BoundedRedisValue Start, BoundedRedisValue Stop, Order Order, long Offset, long Count) request)
         {
             bool writeLimit = request.Offset != 0 || request.Count != long.MaxValue;
             var argCount = 4 + (writeLimit ? 3 : 0) + (_withScores ? 1 : 0) + (request.Order == Order.Descending ? 1 : 0);
             writer.WriteCommand(command, argCount);
             writer.Write(request.Key);
             writer.WriteBulkString(request.Start);
             writer.WriteBulkString(request.Stop);
             writer.WriteRaw("$5\r\nBYLEX\r\n"u8);
             if (request.Order == Order.Descending)
             {
                 writer.WriteRaw("$3\r\nREV\r\n"u8);
             }
             if (writeLimit)
             {
                 writer.WriteRaw("$5\r\nLIMIT\r\n"u8);
                 writer.WriteBulkString(request.Offset);
                 writer.WriteBulkString(request.Count);
             }
             if (_withScores)
             {
                 writer.WriteRaw("$10\r\nWITHSCORES\r\n"u8);
             }
         }
    }

    [RespCommand]
    public static partial RespOperation<RedisValue[]> ZRangeByLex(
        this in SortedSetCommands context,
        RedisKey key,
        BoundedRedisValue min,
        BoundedRedisValue max);

    [RespCommand]
    public static partial RespOperation<RedisValue[]> ZRangeByScore(
        this in SortedSetCommands context,
        RedisKey key,
        BoundedDouble min,
        BoundedDouble max);

    [RespCommand(nameof(ZRangeByScore))]
    public static partial RespOperation<SortedSetEntry[]> ZRangeByScoreWithScores(
        this in SortedSetCommands context,
        RedisKey key,
        BoundedDouble min,
        [RespSuffix("WITHSCORES")] BoundedDouble max);

    [RespCommand]
    public static partial RespOperation<long> ZRangeStore(
        this in SortedSetCommands context,
        RedisKey destination,
        RedisKey source,
        long start,
        long stop);

    [RespCommand]
    public static partial RespOperation<long?> ZRank(
        this in SortedSetCommands context,
        RedisKey key,
        RedisValue member);

    [RespCommand]
    public static partial RespOperation<long> ZRem(
        this in SortedSetCommands context,
        RedisKey key,
        RedisValue member);

    [RespCommand]
    public static partial RespOperation<long> ZRem(
        this in SortedSetCommands context,
        RedisKey key,
        RedisValue[] members);

    [RespCommand]
    public static partial RespOperation<long> ZRemRangeByLex(
        this in SortedSetCommands context,
        RedisKey key,
        BoundedRedisValue min,
        BoundedRedisValue max);

    [RespCommand]
    public static partial RespOperation<long> ZRemRangeByRank(
        this in SortedSetCommands context,
        RedisKey key,
        long start,
        long stop);

    [RespCommand]
    public static partial RespOperation<long> ZRemRangeByScore(
        this in SortedSetCommands context,
        RedisKey key,
        BoundedDouble min,
        BoundedDouble max);

    [RespCommand]
    public static partial RespOperation<RedisValue[]> ZRevRange(
        this in SortedSetCommands context,
        RedisKey key,
        long start,
        long stop);

    [RespCommand(nameof(ZRevRange))]
    public static partial RespOperation<SortedSetEntry[]> ZRevRangeWithScores(
        this in SortedSetCommands context,
        RedisKey key,
        long start,
        [RespSuffix("WITHSCORES")] long stop);

    [RespCommand]
    public static partial RespOperation<RedisValue[]> ZRevRangeByLex(
        this in SortedSetCommands context,
        RedisKey key,
        BoundedRedisValue max,
        BoundedRedisValue min);

    [RespCommand]
    public static partial RespOperation<RedisValue[]> ZRevRangeByScore(
        this in SortedSetCommands context,
        RedisKey key,
        BoundedDouble max,
        BoundedDouble min);

    [RespCommand(nameof(ZRevRangeByScore))]
    public static partial RespOperation<SortedSetEntry[]> ZRevRangeByScoreWithScores(
        this in SortedSetCommands context,
        RedisKey key,
        BoundedDouble max,
        [RespSuffix("WITHSCORES")] BoundedDouble min);

    [RespCommand]
    public static partial RespOperation<long?> ZRevRank(
        this in SortedSetCommands context,
        RedisKey key,
        RedisValue member);

    [RespCommand]
    public static partial RespOperation<double?> ZScore(
        this in SortedSetCommands context,
        RedisKey key,
        RedisValue member);

    [RespCommand]
    public static partial RespOperation<double?[]> ZScore(
        this in SortedSetCommands context,
        RedisKey key,
        RedisValue[] members);

    [RespCommand]
    public static partial RespOperation<RedisValue[]> ZUnion(
        this in SortedSetCommands context,
        RedisKey[] keys,
        [RespPrefix("WEIGHTS")] double[]? weights = null,
        [RespPrefix("AGGREGATE")] Aggregate? aggregate = null);

    [RespCommand]
    public static partial RespOperation<RedisValue[]> ZUnion(
        this in SortedSetCommands context,
        RedisKey first,
        RedisKey second,
        [RespPrefix("AGGREGATE")] Aggregate? aggregate = null);

    [RespCommand(nameof(ZUnion))]
    public static partial RespOperation<SortedSetEntry[]> ZUnionWithScores(
        this in SortedSetCommands context,
        [RespSuffix("WITHSCORES")] RedisKey[] keys,
        [RespPrefix("WEIGHTS")] double[]? weights = null,
        [RespPrefix("AGGREGATE")] Aggregate? aggregate = null);

    [RespCommand(nameof(ZUnion))]
    public static partial RespOperation<SortedSetEntry[]> ZUnionWithScores(
        this in SortedSetCommands context,
        RedisKey first,
        [RespSuffix("WITHSCORES")] RedisKey second,
        [RespPrefix("AGGREGATE")] Aggregate? aggregate = null);

    [RespCommand]
    public static partial RespOperation<long> ZUnionStore(
        this in SortedSetCommands context,
        RedisKey destination,
        RedisKey[] keys,
        [RespPrefix("WEIGHTS")] double[]? weights = null,
        [RespPrefix("AGGREGATE")] Aggregate? aggregate = null);

    [RespCommand]
    public static partial RespOperation<long> ZUnionStore(
        this in SortedSetCommands context,
        RedisKey destination,
        RedisKey first,
        RedisKey second,
        [RespPrefix("AGGREGATE")] Aggregate? aggregate = null);

    internal static RespOperation<RedisValue[]> Combine(
        this in SortedSetCommands context,
        SetOperation operation,
        RedisKey[] keys,
        double[]? weights = null,
        Aggregate? aggregate = null) =>
        operation switch
        {
            SetOperation.Difference => context.ZDiff(keys),
            SetOperation.Intersect => context.ZInter(keys, weights, aggregate),
            SetOperation.Union => context.ZUnion(keys, weights, aggregate),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

    internal static RespOperation<SortedSetEntry[]> CombineWithScores(
        this in SortedSetCommands context,
        SetOperation operation,
        RedisKey[] keys,
        double[]? weights = null,
        Aggregate? aggregate = null) =>
        operation switch
        {
            SetOperation.Difference => context.ZDiffWithScores(keys),
            SetOperation.Intersect => context.ZInterWithScores(keys, weights, aggregate),
            SetOperation.Union => context.ZUnionWithScores(keys, weights, aggregate),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

    internal static RespOperation<long> CombineAndStore(
        this in SortedSetCommands context,
        SetOperation operation,
        RedisKey destination,
        RedisKey[] keys,
        double[]? weights = null,
        Aggregate? aggregate = null) =>
        operation switch
        {
            SetOperation.Difference => context.ZDiffStore(destination, keys),
            SetOperation.Intersect => context.ZInterStore(destination, keys, weights, aggregate),
            SetOperation.Union => context.ZUnionStore(destination, keys, weights, aggregate),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

    internal static RespOperation<RedisValue[]> Combine(
        this in SortedSetCommands context,
        SetOperation operation,
        RedisKey first,
        RedisKey second,
        Aggregate? aggregate = null) =>
        operation switch
        {
            SetOperation.Difference => context.ZDiff(first, second),
            SetOperation.Intersect => context.ZInter(first, second, aggregate),
            SetOperation.Union => context.ZUnion(first, second, aggregate),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

    internal static RespOperation<SortedSetEntry[]> CombineWithScores(
        this in SortedSetCommands context,
        SetOperation operation,
        RedisKey first,
        RedisKey second,
        Aggregate? aggregate = null) =>
        operation switch
        {
            SetOperation.Difference => context.ZDiffWithScores(first, second),
            SetOperation.Intersect => context.ZInterWithScores(first, second, aggregate),
            SetOperation.Union => context.ZUnionWithScores(first, second, aggregate),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

    internal static RespOperation<long> CombineAndStore(
        this in SortedSetCommands context,
        SetOperation operation,
        RedisKey destination,
        RedisKey first,
        RedisKey second,
        Aggregate? aggregate = null) =>
        operation switch
        {
            SetOperation.Difference => context.ZDiffStore(destination, first, second),
            SetOperation.Intersect => context.ZInterStore(destination, first, second, aggregate),
            SetOperation.Union => context.ZUnionStore(destination, first, second, aggregate),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

    public static RespOperation<SortedSetPopResult> ZMPop(
        this in SortedSetCommands context,
        RedisKey[] keys,
        Order order = Order.Ascending,
        long count = 1)
        => order == Order.Ascending ? context.ZMPopMin(keys, count) : context.ZMPopMax(keys, count);

    [RespCommand(nameof(ZMPop))]
    private static partial RespOperation<SortedSetPopResult> ZMPopMin(
        this in SortedSetCommands context,
        [RespPrefix, RespSuffix("MIN")] RedisKey[] keys,
        [RespIgnore(1), RespPrefix("COUNT")] long count);

    [RespCommand(nameof(ZMPop))]
    private static partial RespOperation<SortedSetPopResult> ZMPopMax(
        this in SortedSetCommands context,
        [RespPrefix, RespSuffix("MAX")] RedisKey[] keys,
        [RespIgnore(1), RespPrefix("COUNT")] long count);

    internal static RespOperation<SortedSetEntry?> ZPop(
        this in SortedSetCommands context,
        RedisKey key,
        Order order) =>
        order == Order.Ascending
            ? context.ZPopMin(key)
            : context.ZPopMax(key);

    internal static RespOperation<SortedSetEntry[]> ZPop(
        this in SortedSetCommands context,
        RedisKey key,
        long count,
        Order order) =>
        order == Order.Ascending
            ? context.ZPopMin(key, count)
            : context.ZPopMax(key, count);

    internal static RespOperation<long?> ZRank(
        this in SortedSetCommands context,
        RedisKey key,
        RedisValue member,
        Order order) =>
        order == Order.Ascending
            ? context.ZRank(key, member)
            : context.ZRevRank(key, member);
}
