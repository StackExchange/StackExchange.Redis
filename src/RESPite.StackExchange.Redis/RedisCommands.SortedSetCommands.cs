using System.Buffers;
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

    public abstract class ZRangeRequest
    {
        [Flags]
        internal enum ModeFlags
        {
            None = 0,
            WithScores = 1,
            ByLex = 2,
            ByScore = 4,
        }

        private void DemandType(Type type, string factory)
        {
            if (GetType() != type) Throw(factory);
            static void Throw(string factory) => throw new InvalidOperationException($"The request for this operation must be created via {factory}");
        }

        /// <summary>
        /// Indicates whether the data should be reversed.
        /// </summary>
        public bool Reverse { get; set; }

        /// <summary>
        /// The offset into the sub-range for the matching elements.
        /// </summary>
        public long Offset { get; set; }

        /// <summary>
        /// The number of elements to return. A netative value returns all elements from the <see cref="Offset"/>.
        /// </summary>
        public long Count { get; set; } = -1;

        internal void Write(
            scoped ReadOnlySpan<byte> command,
            ref RespWriter writer,
            RedisKey source,
            RedisKey destination,
            ModeFlags flags)
        {
            bool writeLimit = Offset != 0 || Count >= 0;
            ReadOnlySpan<byte> by = default;
            switch (flags & (ModeFlags.ByLex | ModeFlags.ByScore))
            {
                case ModeFlags.ByLex:
                    DemandType(typeof(ZRangeRequestByLex), nameof(ByLex));
                    break;
                case ModeFlags.ByScore:
                    DemandType(typeof(ZRangeRequestByScore), nameof(ByScore));
                    break;
                default:
                    by = this switch
                    {
                        ZRangeRequestByLex => "$5\r\nBYLEX\r\n"u8,
                        ZRangeRequestByScore => "$7\r\nBYSCORE\r\n"u8,
                        _ => default,
                    };
                    break;
            }

            bool withScores = (flags & ModeFlags.WithScores) != 0;
            int argCount = (by.IsEmpty ? 3 : 4)
                           + (withScores ? 1 : 0)
                           + (Reverse ? 1 : 0) + (writeLimit ? 3 : 0)
                           + (destination.IsNull ? 0 : 1);
            writer.WriteCommand(command, argCount);
            if (!destination.IsNull) writer.Write(destination);
            writer.Write(source);
            WriteStartStop(ref writer);
            if (!by.IsEmpty) writer.WriteRaw(by);
            if (Reverse) writer.WriteRaw("$3\r\nREV\r]\n"u8);
            if (writeLimit)
            {
                writer.WriteRaw("$5\r\nLIMIT\r\n"u8);
                writer.WriteBulkString(Offset);
                writer.WriteBulkString(Count);
            }
            if (withScores) writer.WriteRaw("$10\r\nWITHSCORES\r\n"u8);
        }
        protected abstract void WriteStartStop(ref RespWriter writer);
        private protected ZRangeRequest() { }

        public static ZRangeRequest ByRank(long start, long stop)
            => new ZRangeRequestByRank(start, stop);

        public static ZRangeRequest ByLex(RedisValue start, RedisValue stop, Exclude exclude)
            => new ZRangeRequestByLex(start, stop, exclude);

        public static ZRangeRequest ByScore(double start, double stop, Exclude exclude)
            => new ZRangeRequestByScore(start, stop, exclude);

        private sealed class ZRangeRequestByRank(long start, long stop) : ZRangeRequest
        {
            protected override void WriteStartStop(ref RespWriter writer)
            {
                writer.WriteBulkString(start);
                writer.WriteBulkString(stop);
            }
        }
        private sealed class ZRangeRequestByLex(RedisValue start, RedisValue stop, Exclude exclude) : ZRangeRequest
        {
            protected override void WriteStartStop(ref RespWriter writer)
            {
                Write(ref writer, start, exclude, true);
                Write(ref writer, stop, exclude, false);
            }
        }

        internal static void Write(ref RespWriter writer, in RedisValue value, Exclude exclude, bool isStart)
        {
            bool exclusive = (exclude & (isStart ? Exclude.Start : Exclude.Stop)) != 0;
            if (value.IsNull)
            {
                writer.WriteRaw(isStart ? "$1\r\n-\r\n"u8 : "$1\r\n+\r\n"u8);
            }
            else
            {
                var len = value.GetByteCount();
                byte[]? lease = null;
                var span = len < 128 ? stackalloc byte[128] : (lease = ArrayPool<byte>.Shared.Rent(len));
                span[0] = exclusive ? (byte)'(' : (byte)'[';
                value.CopyTo(span.Slice(1)); // allow for the prefix
                writer.WriteBulkString(span.Slice(0, len + 1));
                if (lease is not null) ArrayPool<byte>.Shared.Return(lease);
            }
        }

        private sealed class ZRangeRequestByScore(double start, double stop, Exclude exclude) : ZRangeRequest
        {
            protected override void WriteStartStop(ref RespWriter writer)
            {
                Write(ref writer, start, exclude, true);
                Write(ref writer, stop, exclude, false);
            }
        }

        internal static void Write(ref RespWriter writer, double value, Exclude exclude, bool isStart)
        {
            bool exclusive = (exclude & (isStart ? Exclude.Start : Exclude.Stop)) != 0;
            if (exclusive)
            {
                writer.WriteBulkStringExclusive(value);
            }
            else
            {
                writer.WriteBulkString(value);
            }
        }
    }
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

    [RespCommand]
    public static partial RespOperation<long> ZCard(this in SortedSetCommands context, RedisKey key);

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

        return context.ZCount(key, min, max, exclude);
    }

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

    [RespCommand(Formatter = "ZCountFormatter.Instance")]
    public static partial RespOperation<long> ZCount(
        this in SortedSetCommands context,
        RedisKey key,
        double min,
        double max,
        Exclude exclude = Exclude.None);

    private sealed class ZCountFormatter : IRespFormatter<(RedisKey Key, double Min, double Max, Exclude Exclude)>
    {
        private ZCountFormatter() { }
        public static readonly ZCountFormatter Instance = new();

        public void Format(
            scoped ReadOnlySpan<byte> command,
            ref RespWriter writer,
            in (RedisKey Key, double Min, double Max, Exclude Exclude) request)
        {
             writer.WriteCommand(command, 3);
             writer.Write(request.Key);
             SortedSetCommands.ZRangeRequest.Write(ref writer, request.Min, request.Exclude, true);
             SortedSetCommands.ZRangeRequest.Write(ref writer, request.Max, request.Exclude, false);
        }
    }

    [RespCommand]
    public static partial RespOperation<RedisValue[]> ZDiff(
        this in SortedSetCommands context,
        RedisKey[] keys);

    [RespCommand]
    public static partial RespOperation<RedisValue[]> ZDiff(
        this in SortedSetCommands context,
        RedisKey first,
        RedisKey second);

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

    [RespCommand(Formatter = "ZLexCountFormatter.Instance")]
    public static partial RespOperation<long> ZLexCount(
        this in SortedSetCommands context,
        RedisKey key,
        RedisValue min,
        RedisValue max,
        Exclude exclude = Exclude.None);

    private sealed class ZLexCountFormatter : IRespFormatter<(RedisKey Key, RedisValue Min, RedisValue Max, Exclude Exclude)>
    {
        private ZLexCountFormatter() { }
        public static readonly ZLexCountFormatter Instance = new();

        public void Format(
            scoped ReadOnlySpan<byte> command,
            ref RespWriter writer,
            in (RedisKey Key, RedisValue Min, RedisValue Max, Exclude Exclude) request)
        {
            writer.WriteCommand(command, 3);
            writer.Write(request.Key);
            SortedSetCommands.ZRangeRequest.Write(ref writer, request.Min, request.Exclude, true);
            SortedSetCommands.ZRangeRequest.Write(ref writer, request.Max, request.Exclude, false);
        }
    }

    [RespCommand(nameof(ZMPop))]
    private static partial RespOperation<SortedSetPopResult> ZMPopMax(
        this in SortedSetCommands context,
        [RespPrefix, RespSuffix("MAX")] RedisKey[] keys,
        [RespIgnore(1), RespPrefix("COUNT")] long count);

    [RespCommand(nameof(ZMPop))]
    private static partial RespOperation<SortedSetPopResult> ZMPopMin(
        this in SortedSetCommands context,
        [RespPrefix, RespSuffix("MIN")] RedisKey[] keys,
        [RespIgnore(1), RespPrefix("COUNT")] long count);

    public static RespOperation<SortedSetPopResult> ZMPop(
        this in SortedSetCommands context,
        RedisKey[] keys,
        Order order = Order.Ascending,
        long count = 1)
        => order == Order.Ascending ? context.ZMPopMin(keys, count) : context.ZMPopMax(keys, count);

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

    private sealed class ZRangeFormatter : IRespFormatter<(RedisKey Key, SortedSetCommands.ZRangeRequest Request)>,
         IRespFormatter<(RedisKey Destination, RedisKey Source, SortedSetCommands.ZRangeRequest Request)>
    {
        private readonly SortedSetCommands.ZRangeRequest.ModeFlags _flags;
        private ZRangeFormatter(SortedSetCommands.ZRangeRequest.ModeFlags flags) => _flags = flags;
        public static readonly ZRangeFormatter NoScores = new(SortedSetCommands.ZRangeRequest.ModeFlags.None);
        public static readonly ZRangeFormatter WithScores = new(SortedSetCommands.ZRangeRequest.ModeFlags.WithScores);
        public static readonly ZRangeFormatter ByLexNoScores = new(SortedSetCommands.ZRangeRequest.ModeFlags.ByLex);
        public static readonly ZRangeFormatter ByLexWithScores = new(SortedSetCommands.ZRangeRequest.ModeFlags.WithScores | SortedSetCommands.ZRangeRequest.ModeFlags.ByLex);
        public static readonly ZRangeFormatter ByScoreNoScores = new(SortedSetCommands.ZRangeRequest.ModeFlags.ByScore);
        public static readonly ZRangeFormatter ByScoreWithScores = new(SortedSetCommands.ZRangeRequest.ModeFlags.WithScores | SortedSetCommands.ZRangeRequest.ModeFlags.ByScore);

        public void Format(
            scoped ReadOnlySpan<byte> command,
            ref RespWriter writer,
            in (RedisKey Key, SortedSetCommands.ZRangeRequest Request) request)
            => request.Request.Write(command,  ref writer, RedisKey.Null, request.Key, _flags);
        public void Format(
            scoped ReadOnlySpan<byte> command,
            ref RespWriter writer,
            in (RedisKey Destination, RedisKey Source, SortedSetCommands.ZRangeRequest Request) request)
            => request.Request.Write(command,  ref writer, request.Destination, request.Source, _flags);
    }

    [RespCommand] // by rank
    public static partial RespOperation<RedisValue[]> ZRange(
        this in SortedSetCommands context,
        RedisKey key,
        long min,
        long max);

    [RespCommand(Formatter = "ZRangeFormatter.NoScores")] // flexible
    public static partial RespOperation<RedisValue[]> ZRange(
        this in SortedSetCommands context,
        RedisKey key,
        SortedSetCommands.ZRangeRequest request);

    internal static RespOperation<RedisValue[]> ZRange(
        this in SortedSetCommands context,
        RedisKey key,
        long min,
        long max,
        Order order) => order == Order.Ascending ? context.ZRange(key, min, max) : context.ZRevRange(key, max, min);

    [RespCommand(nameof(ZRange))] // by rank, with scores
    public static partial RespOperation<SortedSetEntry[]> ZRangeWithScores(
        this in SortedSetCommands context,
        RedisKey key,
        long min,
        [RespSuffix("WITHSCORES")] long max);

    internal static RespOperation<SortedSetEntry[]> ZRangeWithScores(
        this in SortedSetCommands context,
        RedisKey key,
        long min,
        long max,
        Order order) => order == Order.Ascending ? context.ZRangeWithScores(key, min, max) : context.ZRevRangeWithScores(key, max, min);

    [RespCommand(nameof(ZRange), Formatter = "ZRangeFormatter.WithScores")] // flexible, with scores
    public static partial RespOperation<SortedSetEntry[]> ZRangeWithScores(
        this in SortedSetCommands context,
        RedisKey key,
        SortedSetCommands.ZRangeRequest request);

    [RespCommand(Formatter = "ZRangeFormatter.ByLexNoScores")]
    public static partial RespOperation<RedisValue[]> ZRangeByLex(
        this in SortedSetCommands context,
        RedisKey key,
        SortedSetCommands.ZRangeRequest request);

    internal static RespOperation<RedisValue[]> ZRangeByLex(
        this in SortedSetCommands context,
        RedisKey key,
        SortedSetCommands.ZRangeRequest request,
        Order order) => order == Order.Ascending ? context.ZRangeByLex(key, request) : context.ZRevRangeByLex(key, request);

    [RespCommand(nameof(ZRangeByLex), Formatter = "ZRangeFormatter.ByLexWithScores")]
    public static partial RespOperation<SortedSetEntry[]> ZRangeByLexWithScores(
        this in SortedSetCommands context,
        RedisKey key,
        SortedSetCommands.ZRangeRequest request);

    [RespCommand(Formatter = "ZRangeFormatter.ByScoreNoScores")]
    public static partial RespOperation<RedisValue[]> ZRangeByScore(
        this in SortedSetCommands context,
        RedisKey key,
        SortedSetCommands.ZRangeRequest request);

    internal static RespOperation<RedisValue[]> ZRangeByScore(
        this in SortedSetCommands context,
        RedisKey key,
        SortedSetCommands.ZRangeRequest request,
        Order order) => order == Order.Ascending ? context.ZRangeByScore(key, request) : context.ZRevRangeByScore(key, request);

    [RespCommand(nameof(ZRangeByScore), Formatter = "ZRangeFormatter.ByScoreWithScores")]
    public static partial RespOperation<SortedSetEntry[]> ZRangeByScoreWithScores(
        this in SortedSetCommands context,
        RedisKey key,
        SortedSetCommands.ZRangeRequest request);

    internal static RespOperation<SortedSetEntry[]> ZRangeByScoreWithScores(
        this in SortedSetCommands context,
        RedisKey key,
        SortedSetCommands.ZRangeRequest request,
        Order order) => order == Order.Ascending ? context.ZRangeByScoreWithScores(key, request) : context.ZRevRangeByScoreWithScores(key, request);

    [RespCommand] // by rank
    public static partial RespOperation<long> ZRangeStore(
        this in SortedSetCommands context,
        RedisKey destination,
        RedisKey source,
        long min,
        long max);

    [RespCommand(Formatter = "ZRangeFormatter.NoScores")] // flexible
    public static partial RespOperation<long> ZRangeStore(
        this in SortedSetCommands context,
        RedisKey destination,
        RedisKey source,
        SortedSetCommands.ZRangeRequest request);

    internal static RespOperation<long> ZRangeStore(
        this in SortedSetCommands context,
        RedisKey sourceKey,
        RedisKey destinationKey,
        RedisValue start,
        RedisValue stop,
        SortedSetOrder sortedSetOrder,
        Exclude exclude,
        Order order,
        long skip,
        long? take)
    {
        SortedSetCommands.ZRangeRequest request =
            sortedSetOrder switch
            {
                SortedSetOrder.ByRank => SortedSetCommands.ZRangeRequest.ByRank((long)start, (long)stop),
                SortedSetOrder.ByLex => SortedSetCommands.ZRangeRequest.ByLex(start, stop, exclude),
                SortedSetOrder.ByScore => SortedSetCommands.ZRangeRequest.ByScore((double)start, (double)stop, exclude),
                _ => throw new ArgumentOutOfRangeException(nameof(sortedSetOrder)),
            };
        request.Offset = skip;
        if (take is not null) request.Count = take.Value;
        request.Reverse = order == Order.Descending;
        return context.ZRangeStore(destinationKey, sourceKey, request);
    }

    internal static RespOperation<long?> ZRank(
        this in SortedSetCommands context,
        RedisKey key,
        RedisValue member,
        Order order) =>
        order == Order.Ascending
            ? context.ZRank(key, member)
            : context.ZRevRank(key, member);

    [RespCommand]
    public static partial RespOperation<long?> ZRank(
        this in SortedSetCommands context,
        RedisKey key,
        RedisValue member);

    [RespCommand]
    public static partial RespOperation<bool> ZRem(
        this in SortedSetCommands context,
        RedisKey key,
        RedisValue member);

    [RespCommand]
    public static partial RespOperation<long> ZRem(
        this in SortedSetCommands context,
        RedisKey key,
        RedisValue[] members);

    [RespCommand(Formatter = "ZRangeFormatter.ByLexNoScores")]
    public static partial RespOperation<long> ZRemRangeByLex(
        this in SortedSetCommands context,
        RedisKey key,
        SortedSetCommands.ZRangeRequest request);

    [RespCommand]
    public static partial RespOperation<long> ZRemRangeByRank(
        this in SortedSetCommands context,
        RedisKey key,
        long start,
        long stop);

    [RespCommand(Formatter = "ZRangeFormatter.ByScoreNoScores")]
    public static partial RespOperation<long> ZRemRangeByScore(
        this in SortedSetCommands context,
        RedisKey key,
        SortedSetCommands.ZRangeRequest request);

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

    [RespCommand(Formatter = "ZRangeFormatter.ByLexNoScores")]
    public static partial RespOperation<RedisValue[]> ZRevRangeByLex(
        this in SortedSetCommands context,
        RedisKey key,
        SortedSetCommands.ZRangeRequest request);

    [RespCommand(nameof(ZRevRangeByLex), Formatter = "ZRangeFormatter.ByLexWithScores")]
    public static partial RespOperation<SortedSetEntry[]> ZRevRangeByLexWithScores(
        this in SortedSetCommands context,
        RedisKey key,
        SortedSetCommands.ZRangeRequest request);

    [RespCommand(Formatter = "ZRangeFormatter.ByScoreNoScores")]
    public static partial RespOperation<RedisValue[]> ZRevRangeByScore(
        this in SortedSetCommands context,
        RedisKey key,
        SortedSetCommands.ZRangeRequest request);

    [RespCommand(Formatter = "ZRangeFormatter.ByScoreWithScores")]
    public static partial RespOperation<SortedSetEntry[]> ZRevRangeByScoreWithScores(
        this in SortedSetCommands context,
        RedisKey key,
        SortedSetCommands.ZRangeRequest request);

    [RespCommand]
    public static partial RespOperation<long?> ZRevRank(
        this in SortedSetCommands context,
        RedisKey key,
        RedisValue member);

    [RespCommand(Parser = "RespParsers.ZScanSimple")]
    public static partial RespOperation<ScanResult<SortedSetEntry>> ZScan(
        this in SortedSetCommands context,
        RedisKey key,
        long cursor,
        [RespPrefix("MATCH"), RespIgnore] RedisValue pattern = default,
        [RespPrefix("COUNT"), RespIgnore(10)] long count = 10);

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
}
