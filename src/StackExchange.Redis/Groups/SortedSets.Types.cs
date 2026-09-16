using System;
using System.Diagnostics.CodeAnalysis;
using RESPite;
using StackExchange.Redis.Interpolated;

namespace StackExchange.Redis;

// The argument shapes of the sorted-set group. These are IRespArgument structs rather than reply types,
// but what they share with Streams.Types.cs is the reason for the file: they are the group's own, and of
// no use outside it - ZADD's option tokens mean nothing to any other command - so they live beside the
// commands that write them rather than in a shared namespace.

/// <summary>
/// EXPERIMENTAL SPIKE. The option tokens of <c>ZADD</c>: up to six of them, in the order the server
/// documents.
/// </summary>
/// <remarks>
/// The retry category belongs here too, because it depends on the same flags: NX/XX are conditional
/// and GT/LT are monotone, so a replay converges - but <c>INCR</c> compounds on every call unless NX
/// makes a replay a no-op. That is <c>SortedSetAddMessage.GetRetryCategory</c>'s rule, kept with the
/// tokens it belongs to rather than at each of the three call sites.
/// </remarks>
internal readonly struct RespSortedSetOptions : IRespArgument
{
    private const SortedSetWhen Known =
        SortedSetWhen.Exists | SortedSetWhen.GreaterThan | SortedSetWhen.LessThan | SortedSetWhen.NotExists;

    private readonly SortedSetWhen _when;
    private readonly bool _change;
    private readonly bool _increment;

    internal RespSortedSetOptions(SortedSetWhen when, bool change, bool increment)
    {
        if ((when & ~Known) != 0) throw new ArgumentOutOfRangeException(nameof(when));

        _when = when;
        _change = change;
        _increment = increment;
    }

    /// <summary>
    /// ZADD covers three very different side-effect profiles depending on its options, so the
    /// per-command default (last-wins) is only right for the plain form.
    /// </summary>
    internal CommandFlags RetryCategory
    {
        get
        {
            if (!_increment)
            {
                // NX/XX are conditional; GT/LT are monotone, so re-applying the same score converges.
                // A bare ZADD is an unconditional overwrite: leave the per-command default alone.
                return _when == SortedSetWhen.Always ? CommandFlags.None : CommandFlags.CommandRetryWriteChecked;
            }

            // ZADD ... INCR compounds on every call - *unless* NX, where a replay can only find the
            // member present and no-op.
            return (_when & SortedSetWhen.NotExists) != 0
                ? CommandFlags.CommandRetryWriteChecked
                : CommandFlags.CommandRetryWriteAccumulating;
        }
    }

    /// <inheritdoc/>
    public void WriteTo(scoped ref RespRequestBuilder handler)
    {
        if ((_when & SortedSetWhen.NotExists) != 0) handler.AppendFormatted(RespLiterals.Nx);
        if ((_when & SortedSetWhen.Exists) != 0) handler.AppendFormatted(RespLiterals.Xx);
        if ((_when & SortedSetWhen.GreaterThan) != 0) handler.AppendFormatted(RespLiterals.Gt);
        if ((_when & SortedSetWhen.LessThan) != 0) handler.AppendFormatted(RespLiterals.Lt);
        if (_change) handler.AppendFormatted(RespLiterals.Ch);
        if (_increment) handler.AppendFormatted(RespLiterals.Incr);
    }
}

/// <summary>
/// EXPERIMENTAL SPIKE. A <c>ZRANGESTORE</c> bound, which brackets an inclusive LEXICAL bound where the
/// read commands leave it bare.
/// </summary>
/// <remarks>
/// The asymmetry is the server's rather than ours, and it is exactly the kind of thing that gets
/// "tidied up" into a bug - so it lives in one place and is named for what it is.
/// </remarks>
internal readonly struct RespRangeStoreBound : IRespArgument
{
    private readonly RedisValue _value;
    private readonly bool _exclusive;
    private readonly bool _lex;

    private RespRangeStoreBound(in RedisValue value, bool exclusive, bool lex)
    {
        _value = value;
        _exclusive = exclusive;
        _lex = lex;
    }

    /// <summary>The lower bound of a ZRANGESTORE range.</summary>
    internal static RespRangeStoreBound Start(in RedisValue value, Exclude exclude, SortedSetOrder order)
        => new(value, (exclude & Exclude.Start) != 0, order == SortedSetOrder.ByLex);

    /// <summary>The upper bound of a ZRANGESTORE range.</summary>
    internal static RespRangeStoreBound Stop(in RedisValue value, Exclude exclude, SortedSetOrder order)
        => new(value, (exclude & Exclude.Stop) != 0, order == SortedSetOrder.ByLex);

    /// <inheritdoc/>
    public void WriteTo(scoped ref RespRequestBuilder handler)
    {
        if (_exclusive) handler.AppendFormatted(("(" + _value).AsRedisValue());
        else if (_lex) handler.AppendFormatted(("[" + _value).AsRedisValue());
        else handler.AppendFormatted(_value);
    }
}

/// <summary>
/// EXPERIMENTAL SPIKE. The <c>AGGREGATE mode</c> pair, which writes two arguments or none.
/// </summary>
/// <remarks>
/// <c>SUM</c> is the server's own default, so it renders as nothing at all - the same arrangement as
/// BYTE on <c>BITCOUNT</c> and KEEPTTL's absence on SET.
/// </remarks>
internal readonly struct RespAggregate(Aggregate aggregate) : IRespArgument
{
    /// <inheritdoc/>
    public void WriteTo(scoped ref RespRequestBuilder handler)
    {
        switch (aggregate)
        {
            case Aggregate.Sum:
                return;
            case Aggregate.Min:
                handler.AppendFormatted(RespLiterals.Aggregate);
                handler.AppendFormatted(RespLiterals.Min);
                return;
            case Aggregate.Max:
                handler.AppendFormatted(RespLiterals.Aggregate);
                handler.AppendFormatted(RespLiterals.Max);
                return;
            case Aggregate.Count:
                handler.AppendFormatted(RespLiterals.Aggregate);
                handler.AppendFormatted(RespLiterals.Count);
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(aggregate));
        }
    }
}

/// <summary>
/// EXPERIMENTAL SPIKE. The <c>LIMIT offset count</c> triple, which writes three arguments or none.
/// </summary>
/// <remarks>
/// <c>(0, -1)</c> is "everything", which is the server's own behaviour without the operand - so the
/// defaults write nothing at all, and only a caller who asked for a window pays for one.
/// </remarks>
internal readonly struct RespLimitRange(long skip, long take) : IRespArgument
{
    /// <summary>
    /// No window at all - which is <c>(0, -1)</c>, not <c>default</c>.
    /// </summary>
    /// <remarks>
    /// <b>The sentinel is not the zero value</b>, and that is worth a name rather than a literal: a
    /// <c>default</c> instance is <c>(0, 0)</c>, which is a perfectly meaningful window of nothing, and
    /// writing <c>default</c> where "no window" was meant renders <c>LIMIT 0 0</c> and quietly returns
    /// an empty range. Found exactly that way.
    /// </remarks>
    internal static RespLimitRange None => new(0, -1);

    /// <inheritdoc/>
    public void WriteTo(scoped ref RespRequestBuilder handler)
    {
        if (skip == 0 && take == -1) return; // the server's own behaviour without the operand

        handler.AppendFormatted(RespLiterals.Limit);
        handler.AppendFormatted((RedisValue)skip);
        handler.AppendFormatted((RedisValue)take);
    }
}
