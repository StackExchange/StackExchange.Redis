using System;

namespace StackExchange.Redis;

/// <summary>
/// The result of a LongestCommonSubsequence command with IDX feature.
/// Returns a list of the positions of each sub-match.
/// </summary>
public readonly struct LCSMatchResult
{
    internal static LCSMatchResult Null { get; } = new LCSMatchResult(Array.Empty<LCSMatch>(), 0);

    /// <summary>
    /// Whether this match result contains any matches.
    /// </summary>
    public bool IsEmpty => LongestMatchLength == 0 && (Matches is null || Matches.Length == 0);

    /// <summary>
    /// The matched positions of all the sub-matched strings.
    /// </summary>
    public LCSMatch[] Matches { get; }

    /// <summary>
    /// The length of the longest match.
    /// </summary>
    public long LongestMatchLength { get; }

    /// <summary>
    /// Returns a new <see cref="LCSMatchResult"/>.
    /// </summary>
    /// <param name="matches">The matched positions in each string.</param>
    /// <param name="matchLength">The length of the match.</param>
    internal LCSMatchResult(LCSMatch[] matches, long matchLength)
    {
        Matches = matches;
        LongestMatchLength = matchLength;
    }

    /// <summary>
    /// Represents a sub-match of the longest match. i.e first indexes the matched substring in each string.
    /// </summary>
    public readonly struct LCSMatch
    {
        /// <summary>
        /// The first index of the matched substring in the first string.
        /// </summary>
        public long FirstStringIndex { get; }

        /// <summary>
        /// The first index of the matched substring in the second string.
        /// </summary>
        public long SecondStringIndex { get; }

        /// <summary>
        /// The length of the match.
        /// </summary>
        public long Length { get; }

        /// <summary>
        /// Returns a new Match.
        /// </summary>
        /// <param name="firstStringIndex">The first index of the matched substring in the first string.</param>
        /// <param name="secondStringIndex">The first index of the matched substring in the second string.</param>
        /// <param name="length">The length of the match.</param>
        internal LCSMatch(long firstStringIndex, long secondStringIndex, long length)
        {
            FirstStringIndex = firstStringIndex;
            SecondStringIndex = secondStringIndex;
            Length = length;
        }
    }
}
