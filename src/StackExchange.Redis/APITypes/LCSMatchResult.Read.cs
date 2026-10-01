using RESPite.Messages;

// ReSharper disable once CheckNamespace
namespace StackExchange.Redis;

public readonly partial struct LCSMatchResult
{
    /// <summary>
    /// Read an <c>LCS ... IDX</c> reply: <c>["matches", [...], "len", n]</c>.
    /// </summary>
    /// <param name="reader">The reader, positioned on the top-level aggregate.</param>
    /// <param name="result">The parsed result, when this returns <see langword="true"/>.</param>
    /// <remarks>
    /// <para>
    /// Shared by both readers - the <c>ResultProcessor</c> path and the interpolated surface's handler -
    /// rather than restated in each, for the same reason <see cref="Expiration"/> shares its operand
    /// switch between the two writers: this is the whole of the shape knowledge, and it is exactly the
    /// part that would silently diverge if either kept its own copy.
    /// </para>
    /// <para>
    /// The fields are read <b>nominally</b>, not positionally: the reply is documented as a map-shaped
    /// array and the server is free to order it as it likes.
    /// </para>
    /// </remarks>
    internal static bool TryRead(ref RespReader reader, out LCSMatchResult result)
    {
        result = default;
        if (!reader.IsAggregate) return false;

        LCSMatch[]? matchesArray = null;
        long longestMatchLength = 0;

        var iter = reader.AggregateChildren();
        while (iter.MoveNext() && iter.Value.IsScalar)
        {
            LCSField field;
            unsafe
            {
                if (!iter.Value.TryParseScalar(&LCSFieldMetadata.TryParse, out field))
                {
                    field = LCSField.Unknown;
                }
            }

            if (!iter.MoveNext()) break; // out of data

            switch (field)
            {
                case LCSField.Matches:
                    if (iter.Value.IsAggregate)
                    {
                        bool failed = false;
                        matchesArray = iter.Value.ReadPastArray(ref failed, static (ref failed, ref reader) =>
                        {
                            // Don't even bother if we've already failed
                            if (!failed && reader.IsAggregate)
                            {
                                var matchChildren = reader.AggregateChildren();
                                if (matchChildren.MoveNext() && TryReadPosition(ref matchChildren.Value, out var firstPos)
                                    && matchChildren.MoveNext() && TryReadPosition(ref matchChildren.Value, out var secondPos)
                                    && matchChildren.MoveNext() && matchChildren.Value.IsScalar && matchChildren.Value.TryReadInt64(out var length))
                                {
                                    return new LCSMatch(firstPos, secondPos, length);
                                }
                            }
                            failed = true;
                            return default;
                        });

                        // Check if anything went wrong
                        if (failed) matchesArray = null;
                    }
                    break;

                case LCSField.Len:
                    if (iter.Value.IsScalar)
                    {
                        longestMatchLength = iter.Value.TryReadInt64(out var totalLen) ? totalLen : 0;
                    }
                    break;
            }
        }

        if (matchesArray is null) return false;

        result = new LCSMatchResult(matchesArray, longestMatchLength);
        return true;
    }

    private static bool TryReadPosition(ref RespReader reader, out LCSPosition position)
    {
        // Expecting a 2-element array: [start, end]
        position = default;
        if (!reader.IsAggregate) return false;

        if (!(reader.TryMoveNext() && reader.IsScalar && reader.TryReadInt64(out var start))) return false;

        if (!(reader.TryMoveNext() && reader.IsScalar && reader.TryReadInt64(out var end))) return false;

        position = new LCSPosition(start, end);
        return true;
    }
}
