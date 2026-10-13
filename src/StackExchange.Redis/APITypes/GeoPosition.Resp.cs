using RESPite.Messages;

// ReSharper disable once CheckNamespace
namespace StackExchange.Redis;

public readonly partial struct GeoPosition
{
    /// <summary>
    /// Read a coordinate pair: <c>[longitude, latitude]</c>, or a nil where the member is unknown.
    /// </summary>
    /// <param name="reader">The reader, positioned on the element.</param>
    /// <remarks>
    /// Shared by both readers - the <c>ResultProcessor</c> path and the interpolated surface's handler -
    /// rather than restated in each: anything less than a two-element aggregate of two doubles is a
    /// missing member rather than a malformed reply, and that judgement is the whole of the shape.
    /// </remarks>
    internal static GeoPosition? TryRead(ref RespReader reader)
    {
        if (reader.IsAggregate && reader.AggregateLengthIs(2)
            && reader.TryMoveNext() && reader.IsScalar && reader.TryReadDouble(out var longitude)
            && reader.TryMoveNext() && reader.IsScalar && reader.TryReadDouble(out var latitude)
            && !reader.TryMoveNext())
        {
            return new GeoPosition(longitude, latitude);
        }

        return null;
    }
}
