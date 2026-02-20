using System;
using System.ComponentModel;

// ReSharper disable once CheckNamespace
namespace StackExchange.Redis;

/// <summary>
/// The result of a LongestCommonSubsequence command with IDX feature.
/// Returns a list of the positions of each sub-match.
/// </summary>
// ReSharper disable once InconsistentNaming
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
    /// Represents a position range in a string.
    /// </summary>
    // ReSharper disable once InconsistentNaming
    public readonly struct LCSPosition : IEquatable<LCSPosition>
    {
        /// <summary>
        /// The start index of the position.
        /// </summary>
        public long Start { get; }

        /// <summary>
        /// The end index of the position.
        /// </summary>
        public long End { get; }

        /// <summary>
        /// Returns a new Position.
        /// </summary>
        /// <param name="start">The start index.</param>
        /// <param name="end">The end index.</param>
        public LCSPosition(long start, long end)
        {
            Start = start;
            End = end;
        }

        /// <inheritdoc/>
        public override string ToString() => $"[{Start}..{End}]";

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                return ((int)Start * 31) + (int)End;
            }
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is LCSPosition other && Equals(in other);

        /// <summary>
        /// Compares this position to another for equality.
        /// </summary>
        [CLSCompliant(false)]
        public bool Equals(in LCSPosition other) => Start == other.Start && End == other.End;

        /// <summary>
        /// Compares this position to another for equality.
        /// </summary>
        bool IEquatable<LCSPosition>.Equals(LCSPosition other) => Equals(in other);
    }

    /// <summary>
    /// Represents a sub-match of the longest match. i.e first indexes the matched substring in each string.
    /// </summary>
    // ReSharper disable once InconsistentNaming
    public readonly struct LCSMatch : IEquatable<LCSMatch>
    {
        private readonly LCSPosition _first;
        private readonly LCSPosition _second;

        /// <summary>
        /// The position of the matched substring in the first string.
        /// </summary>
        public LCSPosition First => _first;

        /// <summary>
        /// The position of the matched substring in the second string.
        /// </summary>
        public LCSPosition Second => _second;

        /// <summary>
        /// The first index of the matched substring in the first string.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        [Browsable(false)]
        public long FirstStringIndex => _first.Start;

        /// <summary>
        /// The first index of the matched substring in the second string.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        [Browsable(false)]
        public long SecondStringIndex => _second.Start;

        /// <summary>
        /// The length of the match.
        /// </summary>
        public long Length { get; }

        /// <summary>
        /// Returns a new Match.
        /// </summary>
        /// <param name="first">The position of the matched substring in the first string.</param>
        /// <param name="second">The position of the matched substring in the second string.</param>
        /// <param name="length">The length of the match.</param>
        internal LCSMatch(in LCSPosition first, in LCSPosition second, long length)
        {
            _first = first;
            _second = second;
            Length = length;
        }

        /// <inheritdoc/>
        public override string ToString() => $"First: {_first}, Second: {_second}, Length: {Length}";

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + _first.GetHashCode();
                hash = (hash * 31) + _second.GetHashCode();
                hash = (hash * 31) + Length.GetHashCode();
                return hash;
            }
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is LCSMatch other && Equals(in other);

        /// <summary>
        /// Compares this match to another for equality.
        /// </summary>
        [CLSCompliant(false)]
        public bool Equals(in LCSMatch other) => _first.Equals(in other._first) && _second.Equals(in other._second) && Length == other.Length;

        /// <summary>
        /// Compares this match to another for equality.
        /// </summary>
        bool IEquatable<LCSMatch>.Equals(LCSMatch other) => Equals(in other);
    }
}
