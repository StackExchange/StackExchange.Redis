using RESPite.Messages;

// ReSharper disable once CheckNamespace
namespace StackExchange.Redis;

public readonly partial struct GeoRadiusResult
{
    /// <summary>
    /// Read one result of a geo query, whose shape depends on what the request asked for.
    /// </summary>
    /// <param name="reader">The reader, positioned on the element.</param>
    /// <param name="options">The options the request was made with; they decide the reply's shape.</param>
    /// <remarks>
    /// <para>
    /// Shared by both readers - the <c>ResultProcessor</c> path and the interpolated surface's handler -
    /// rather than restated in each. This one earns it twice over: the reply is a flat array of names
    /// with no options at all, and an array of arrays with any of them, in a fixed order that is not the
    /// order the options are declared in.
    /// </para>
    /// <para>
    /// The order is the server's: the member, then the distance, then the geohash, then the coordinates -
    /// each present only if asked for.
    /// </para>
    /// </remarks>
    internal static GeoRadiusResult Read(ref RespReader reader, GeoRadiusOptions options)
    {
        if (options == GeoRadiusOptions.None)
        {
            // with no WITH option the command just returns names: ["New York", "Milan", "Paris"]
            return new GeoRadiusResult(reader.ReadRedisValue(), null, null, null);
        }

        if (!reader.IsAggregate) return default;

        reader.MoveNext(); // into the sub-array

        // the first item of the sub-array is always the member
        var member = reader.ReadRedisValue();

        double? distance = null;
        GeoPosition? position = null;
        long? hash = null;

        if ((options & GeoRadiusOptions.WithDistance) != 0)
        {
            reader.MoveNextScalar();
            distance = reader.ReadDouble();
        }

        if ((options & GeoRadiusOptions.WithGeoHash) != 0)
        {
            reader.MoveNextScalar();
            hash = reader.TryReadInt64(out var h) ? h : null;
        }

        if ((options & GeoRadiusOptions.WithCoordinates) != 0)
        {
            reader.MoveNextAggregate();
            position = GeoPosition.TryRead(ref reader);
        }

        return new GeoRadiusResult(member, distance, hash, position);
    }
}
