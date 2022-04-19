using System;

namespace StackExchange.Redis;

/// <summary>
/// LCS command options.
/// </summary>
[Flags]
public enum LCSOptions
{
    /// <summary>
    /// No Options.
    /// </summary>
    None = 0,
    /// <summary>
    /// Indicates that we need just the length of the match without the resulted string.
    /// </summary>
    Length = 1,
    /// <summary>
    /// Can be used to include the match positions from each string.
    /// </summary>
    WithMatchedPositions = 2,
}

/// <summary>
/// The result of a LongestCommonSubsequence command.
/// </summary>
public readonly struct LCSMatchResult
{
    /// <summary>
    /// The matched subsequence.
    /// </summary>
    public String? MatchedString { get; }

    /// <summary>
    /// The matched positions of all the sub-matched strings in each key/string.
    /// </summary>
    public MatchedPosition[]? Matcheds { get; }

    /// <summary>
    /// The length of the longest match.
    /// </summary>
    public long? MatchLength { get; }

    /// <summary>
    /// Returns a new LCSMatchResult.
    /// </summary>
    /// <param name="matchedString">The matched string.</param>
    /// <param name="matcheds">The matched positions in each string.</param>
    /// <param name="matchLength">The length of the match.</param>
    public LCSMatchResult(String? matchedString, MatchedPosition[]? matcheds, long? matchLength)
    {
        MatchedString = matchedString;
        Matcheds = matcheds;
        MatchLength = matchLength;
    }
}

/// <summary>
/// Represents a sub-match of the longest match. i.e the matched substring positions in each string.
/// </summary>
public class MatchedPosition
{
    /// <summary>
    /// The sub-match range in the first string.
    /// </summary>
    public PositionRange FirstStringPositionRange { get; }

    /// <summary>
    /// The sub-match range in the second string.
    /// </summary>
    private PositionRange SecondStringPositionRange { get; }

    /// <summary>
    /// The length sub-match.
    /// </summary>
    public long RangeLength { get; }

    /// <summary>
    /// Returns a new MatchedPosition.
    /// </summary>
    /// <param name="a">The matched substring range in the first string.</param>
    /// <param name="b">The matched substring range in the second string.</param>
    /// <param name="rangeLength">The length of the sub-match.</param>
    public MatchedPosition(PositionRange a, PositionRange b, long? rangeLength = null)
    {
        this.FirstStringPositionRange = a;
        this.SecondStringPositionRange = b;
        this.RangeLength = rangeLength is null ? (a.End - a.Start + 1) : rangeLength.Value;
    }
}

/// <summary>
/// Position range.
/// </summary>
public class PositionRange
{
    /// <summary>
    /// The first index of the sub-match.
    /// </summary>
    public long Start { get; }

    /// <summary>
    /// The last index of the sub-match (included).
    /// </summary>
    public long End { get; }

    /// <summary>
    /// Returns a new PositionRange.
    /// </summary>
    /// <param name="start">The first index of the sub-match.</param>
    /// <param name="end">The last index of the sub-match (included).</param>
    public PositionRange(long start, long end)
    {
        Start = start;
        End = end;
    }
}

