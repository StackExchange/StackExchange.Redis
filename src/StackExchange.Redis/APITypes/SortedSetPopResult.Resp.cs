using RESPite.Messages;

// ReSharper disable once CheckNamespace
namespace StackExchange.Redis;

public readonly partial struct SortedSetPopResult
{
    /// <summary>
    /// Read a <c>ZMPOP</c> reply: <c>[key, [[element, score], ...]]</c>, or nil when no key had anything.
    /// </summary>
    /// <param name="reader">The reader, positioned on the reply.</param>
    /// <param name="result">The parsed result, when this returns <see langword="true"/>.</param>
    /// <remarks>
    /// Shared by both readers - the <c>ResultProcessor</c> path and the interpolated surface's handler -
    /// so neither can drift; see <see cref="LCSMatchResult.TryRead"/> for the same arrangement.
    /// </remarks>
    internal static bool TryRead(ref RespReader reader, out SortedSetPopResult result)
    {
        result = Null;
        if (!reader.IsAggregate) return false;

        // RESP3 pure null, or a RESP2 null array: nothing was popped, which is not a failure
        if (reader.IsNull) return true;

        if (reader.TryMoveNext() && reader.IsScalar)
        {
            var key = reader.ReadRedisKey();
            if (reader.TryMoveNext() && reader.IsAggregate)
            {
                var entries = reader.ReadPastArray(
                    static (ref r) => SortedSetEntry.ReadPair(ref r),
                    scalar: false);

                result = new SortedSetPopResult(key, entries!);
                return true;
            }
        }

        return false;
    }
}
