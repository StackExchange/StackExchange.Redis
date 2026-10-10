using RESPite.Messages;

// ReSharper disable once CheckNamespace
namespace StackExchange.Redis;

public readonly partial struct ListPopResult
{
    /// <summary>
    /// Read an <c>LMPOP</c> reply: <c>[key, [value, ...]]</c>, or nil when no key had anything.
    /// </summary>
    /// <param name="reader">The reader, positioned on the reply.</param>
    /// <param name="result">The parsed result, when this returns <see langword="true"/>.</param>
    /// <remarks>
    /// Shared by both readers - the <c>ResultProcessor</c> path and the interpolated surface's handler -
    /// so neither can drift; see <see cref="SortedSetPopResult.TryRead"/>, whose reply differs only in
    /// that its elements are pairs.
    /// </remarks>
    internal static bool TryRead(ref RespReader reader, out ListPopResult result)
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
                result = new ListPopResult(key, reader.ReadPastRedisValues()!);
                return true;
            }
        }

        return false;
    }
}
