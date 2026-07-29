using System;

namespace StackExchange.Redis;

/// <summary>
/// Specifies the ordering of the elements moved (and returned) when moving multiple list elements via
/// <c>LMOVEM</c> (see <see cref="IDatabase.ListMove(RedisKey, RedisKey, ListSide, ListSide, long, ListMoveCount, ListMoveOrder, CommandFlags)"/>).
/// </summary>
public enum ListMoveOrder
{
    /// <summary>
    /// Move the elements as a single block (<c>BULK</c>), preserving the source list's relative order.
    /// </summary>
    Bulk,

    /// <summary>
    /// Move the elements one-by-one (<c>OBO</c>), as if each was popped from the source and pushed to the
    /// destination individually.
    /// </summary>
    OneByOne,
}

internal static class ListMoveOrderExtensions
{
    internal static RedisValue ToLiteral(this ListMoveOrder order) => order switch
    {
        ListMoveOrder.Bulk => RedisLiterals.BULK,
        ListMoveOrder.OneByOne => RedisLiterals.OBO,
        _ => throw new ArgumentOutOfRangeException(nameof(order)),
    };
}
