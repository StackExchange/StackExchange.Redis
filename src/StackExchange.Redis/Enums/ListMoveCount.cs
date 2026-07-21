using System;

namespace StackExchange.Redis;

/// <summary>
/// Specifies how the requested count is interpreted when moving multiple list elements via
/// <c>LMOVEM</c> (see <see cref="IDatabase.ListMove(RedisKey, RedisKey, ListSide, ListSide, long, ListMoveCount, ListMoveOrder, CommandFlags)"/>).
/// </summary>
public enum ListMoveCount
{
    /// <summary>
    /// Move <em>up to</em> the requested number of elements (<c>COUNT</c>); fewer are moved when the
    /// source list has fewer elements, and a null result is returned when the source is empty.
    /// </summary>
    UpTo,

    /// <summary>
    /// Move <em>exactly</em> the requested number of elements (<c>EXACTLY</c>); if the source list does
    /// not have that many elements, nothing is moved and a null result is returned.
    /// </summary>
    Exactly,
}

internal static class ListMoveCountExtensions
{
    internal static RedisValue ToLiteral(this ListMoveCount mode) => mode switch
    {
        ListMoveCount.UpTo => RedisLiterals.COUNT,
        ListMoveCount.Exactly => RedisLiterals.EXACTLY,
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };
}
