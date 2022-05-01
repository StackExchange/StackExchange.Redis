namespace StackExchange.Redis;

/// <summary>
/// The result of a LongestCommonSubsequence command.
/// </summary>
public readonly struct LCSMatchResult
{
    /// <summary>
    /// The matched positions of all the sub-matched strings in each key/string.
    /// </summary>
    public Match[] Matches { get; }

    /// <summary>
    /// The length of the longest match.
    /// </summary>
    public long MatchLength { get; }

    /// <summary>
    /// Returns a new LCSMatchResult.
    /// </summary>
    /// <param name="matches">The matched positions in each string.</param>
    /// <param name="matchLength">The length of the match.</param>
    public LCSMatchResult(Match[] matches, long matchLength)
    {
        Matches = matches;
        MatchLength = matchLength;
    }
}

/// <summary>
/// Represents a sub-match of the longest match. i.e the matched substring positions in each string.
/// </summary>
public class Match
{
    /// <summary>
    /// The first index of the matched substring in the first string.
    /// </summary>
    public long FirstStringIndex { get; }

    /// <summary>
    /// The first index of the matched substring in the second string.
    /// </summary>
    private long SecondStringIndex { get; }

    /// <summary>
    /// The length of the match.
    /// </summary>
    public long Length { get; }

    /// <summary>
    /// Returns a new Match.
    /// </summary>
    /// <param name="a">The first index of the matched substring in the first string.</param>
    /// <param name="b">The first index of the matched substring in the second string.</param>
    /// <param name="length">The length of the match.</param>
    public Match(long a, long b, long length)
    {
        this.FirstStringIndex = a;
        this.SecondStringIndex = b;
        this.Length = length;
    }
}
