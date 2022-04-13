using System.Collections.Generic;

namespace StackExchange.Redis;

/// <summary>
/// A Shape that you can use for a GeoSearch
/// </summary>
public abstract class GeoSearchShape
{
    /// <summary>
    /// The unit to use for creating the shape.
    /// </summary>
    protected GeoUnit Unit { get; }

    /// <summary>
    /// The number of shape arguments.
    /// </summary>
    internal abstract int ArgCount { get; }

    /// <summary>
    /// constructs a <see cref="GeoSearchShape"/>
    /// </summary>
    /// <param name="unit"></param>
    public GeoSearchShape(GeoUnit unit)
    {
        Unit = unit;
    }

    private int NumRadiusOptions(GeoRadiusOptions options)
    {
        var i = 0;
        if ((options & GeoRadiusOptions.WithCoordinates) != 0)
        {
            i++;
        }

        if ((options & GeoRadiusOptions.WithDistance) != 0)
        {
            i++;
        }

        if ((options & GeoRadiusOptions.WithGeoHash) != 0)
        {
            i++;
        }

        return i;
    }

    internal int SearchArgumentCount(RedisKey destinationKey, RedisValue? member, int count, Order? order, GeoRadiusOptions options, bool demandClosest, bool storeDistances)
    {
        return 1 + (!destinationKey.IsNull ? 1 : 0) + (member != RedisValue.Null ? 1 : 2) + (count >= 0 ? 1 : 0) + (order != null ? 1 : 0) + NumRadiusOptions(options) + (!demandClosest ? 1 : 0) + (storeDistances ? 1 : 0);
    }

    internal abstract IEnumerable<RedisValue> GetArgs();
}

/// <summary>
/// A circle drawn on a map bounding
/// </summary>
public class GeoSearchCircle : GeoSearchShape
{
    private readonly double _radius;

    /// <summary>
    /// Creates a <see cref="GeoSearchCircle"/> Shape.
    /// </summary>
    /// <param name="radius">The radius of the circle.</param>
    /// <param name="unit">The distance unit the circle will use, defaults to Meters.</param>
    public GeoSearchCircle(double radius, GeoUnit unit = GeoUnit.Meters) : base (unit)
    {
        _radius = radius;
    }

    internal override int ArgCount => 3;

    /// <summary>
    /// Gets the <exception cref="RedisValue"/>s for this shape
    /// </summary>
    /// <returns></returns>
    internal override IEnumerable<RedisValue> GetArgs()
    {
        yield return RedisLiterals.BYRADIUS;
        yield return _radius;
        yield return Unit.ToLiteral();
    }
}

/// <summary>
/// A box drawn on a map
/// </summary>
public class GeoSearchBox : GeoSearchShape
{
    private readonly double _height;

    private readonly double _width;

    /// <summary>
    /// Initializes a GeoBox.
    /// </summary>
    /// <param name="height">The height of the box.</param>
    /// <param name="width">The width of the box.</param>
    /// <param name="unit">The distance unit the box will use, defaults to Meters.</param>
    public GeoSearchBox(double height, double width, GeoUnit unit = GeoUnit.Meters) : base(unit)
    {
        _height = height;
        _width = width;
    }

    internal override int ArgCount => 4;

    internal override IEnumerable<RedisValue> GetArgs()
    {
        yield return RedisLiterals.BYBOX;
        yield return _width;
        yield return _height;
        yield return Unit.ToLiteral();
    }
}
