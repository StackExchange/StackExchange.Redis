using RESPite.Messages;

// ReSharper disable once CheckNamespace
namespace StackExchange.Redis;

public readonly partial struct SortedSetEntry : Interpolated.IRespArgument
{
    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// Two arguments, <b>score then element</b> - the order <c>ZADD</c> wants them in, and the reverse of
    /// how the type reads. Worth stating out loud: this is the one entry type whose wire order is not its
    /// declaration order, and a hole that emitted them the other way round would produce a command the
    /// server accepts and misinterprets.
    /// </para>
    /// <para>
    /// Explicit, as on <see cref="HashEntry"/> and for the same reason; reached only through a command
    /// hole, where a whole run of entries is one <c>{values}</c>.
    /// </para>
    /// </remarks>
    void Interpolated.IRespArgument.WriteTo(scoped ref Interpolated.RespRequestBuilder handler)
    {
        handler.AppendFormatted((RedisValue)score);
        handler.AppendFormatted(element);
    }

    /// <summary>
    /// Read a <c>[element, score]</c> pair, as <c>ZPOPMIN</c>/<c>ZPOPMAX</c> reply with.
    /// </summary>
    /// <param name="reader">The reader, positioned on the aggregate.</param>
    /// <param name="result">The entry, or <see langword="null"/> for an empty or null reply.</param>
    /// <remarks>
    /// Shared by both readers - the <c>ResultProcessor</c> path and the interpolated surface's handler -
    /// so the two cannot disagree about the same bytes. See <see cref="LCSMatchResult.TryRead"/> for the
    /// same arrangement.
    /// </remarks>
    internal static bool TryRead(ref RespReader reader, out SortedSetEntry? result)
    {
        result = null;
        if (!reader.IsAggregate) return false;

        // Note: null arrays report false for TryMoveNext, so no explicit null check needed
        if (reader.TryMoveNext() && reader.IsScalar)
        {
            var element = reader.ReadRedisValue();
            if (reader.TryMoveNext() && reader.IsScalar)
            {
                var score = reader.TryReadDouble(out var val) ? val : double.NaN;
                result = new SortedSetEntry(element, score);
            }
        }

        return true;
    }

    /// <summary>Read one <c>[element, score]</c> pair from within a run of them.</summary>
    /// <param name="reader">The reader, positioned on the pair.</param>
    internal static SortedSetEntry ReadPair(ref RespReader reader)
    {
        if (reader.IsAggregate && reader.TryMoveNext() && reader.IsScalar)
        {
            var element = reader.ReadRedisValue();
            if (reader.TryMoveNext() && reader.IsScalar)
            {
                var score = reader.TryReadDouble(out var val) ? val : double.NaN;
                return new SortedSetEntry(element, score);
            }
        }

        return default;
    }
}
