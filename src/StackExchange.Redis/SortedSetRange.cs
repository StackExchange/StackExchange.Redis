using System;

namespace StackExchange.Redis;

/// <summary>
/// A range of a sorted set: by rank, by score, or by member value, with the bound exclusivity where the
/// axis has one. One operand replaces the per-axis method families (<c>RangeByRank...</c>,
/// <c>RangeByScore...</c>, <c>RangeByValue...</c>) the old surface accumulated: the axis is data, and
/// every operation that walks a range (<see cref="SortedSets.RangeAsync"/>,
/// <see cref="SortedSets.RemoveRangeAsync"/>, <see cref="SortedSets.RangeAndStoreAsync"/>,
/// <see cref="SortedSets.RangeLengthAsync"/>) takes the same one.
/// </summary>
/// <remarks>
/// Create one with <see cref="ByRank"/>, <see cref="ByScore"/> or <see cref="ByValue"/>. A
/// <see langword="default"/> instance is the whole set, by rank. Direction, and where the axis supports
/// them, paging, are not part of the range: they are parameters of the read, because the remove and
/// length forms have no server spelling for them.
/// </remarks>
public readonly struct SortedSetRange
{
    internal enum RangeAxis : byte
    {
        Rank = 0, // the default-instance axis, so default(SortedSetRange) means "everything"
        Score = 1,
        Value = 2,
    }

    private readonly RangeAxis _axis;
    private readonly Exclude _exclude;
    private readonly long _rankStart;
    private readonly long _rankStopComplement; // ~stop, so the default instance (0) reads back as -1: the whole set
    private readonly double _scoreMin;
    private readonly double _scoreMax;
    private readonly RedisValue _valueMin;
    private readonly RedisValue _valueMax;

    private SortedSetRange(RangeAxis axis, Exclude exclude, long rankStart, long rankStop, double scoreMin, double scoreMax, RedisValue valueMin, RedisValue valueMax)
    {
        _axis = axis;
        _exclude = exclude;
        _rankStart = rankStart;
        _rankStopComplement = ~rankStop;
        _scoreMin = scoreMin;
        _scoreMax = scoreMax;
        _valueMin = valueMin;
        _valueMax = valueMax;
    }

    internal RangeAxis Axis => _axis;

    internal Exclude Exclude => _exclude;

    internal long RankStart => _rankStart;

    internal long RankStop => ~_rankStopComplement;

    internal double ScoreMin => _axis == RangeAxis.Score ? _scoreMin : double.NegativeInfinity;

    internal double ScoreMax => _axis == RangeAxis.Score ? _scoreMax : double.PositiveInfinity;

    internal RedisValue ValueMin => _valueMin;

    internal RedisValue ValueMax => _valueMax;

    /// <summary>A range of positions: <paramref name="start"/> through <paramref name="stop"/>, both inclusive, negative counting from the end.</summary>
    /// <param name="start">The first rank to take; negative counts from the end.</param>
    /// <param name="stop">The last rank to take; negative counts from the end, and the default of -1 is the last member.</param>
    /// <remarks>
    /// There is no exclusivity here because ranks have no spelling for it on the server; an exclusive
    /// position is simply the next position.
    /// </remarks>
    public static SortedSetRange ByRank(long start = 0, long stop = -1)
        => new(RangeAxis.Rank, Exclude.None, start, stop, default, default, default, default);

    /// <summary>A range of scores: <paramref name="min"/> through <paramref name="max"/>, inclusive except as <paramref name="exclude"/> says.</summary>
    /// <param name="min">The lowest score to take; the default is unbounded.</param>
    /// <param name="max">The highest score to take; the default is unbounded.</param>
    /// <param name="exclude">Which bounds are exclusive.</param>
    public static SortedSetRange ByScore(double min = double.NegativeInfinity, double max = double.PositiveInfinity, Exclude exclude = Exclude.None)
        => new(RangeAxis.Score, exclude, default, default, min, max, default, default);

    /// <summary>
    /// A range of member values, for a set whose members all share one score: <paramref name="min"/>
    /// through <paramref name="max"/> in lexical order, inclusive except as <paramref name="exclude"/>
    /// says. A default (null) bound is unbounded at that end.
    /// </summary>
    /// <param name="min">The lowest member to take; default for unbounded.</param>
    /// <param name="max">The highest member to take; default for unbounded.</param>
    /// <param name="exclude">Which bounds are exclusive.</param>
    public static SortedSetRange ByValue(RedisValue min = default, RedisValue max = default, Exclude exclude = Exclude.None)
        => new(RangeAxis.Value, exclude, default, default, default, default, min, max);

    /// <inheritdoc/>
    public override string ToString() => _axis switch
    {
        RangeAxis.Score => $"scores {_scoreMin}..{_scoreMax} ({_exclude})",
        RangeAxis.Value => $"values {_valueMin}..{_valueMax} ({_exclude})",
        _ => $"ranks {RankStart}..{RankStop}",
    };
}
