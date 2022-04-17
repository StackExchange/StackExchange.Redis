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

    internal abstract void AddArgs(List<RedisValue> args);
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
    internal override void AddArgs(List<RedisValue> args)
    {
        args.Add(RedisLiterals.BYRADIUS);
        args.Add(_radius);
        args.Add(Unit.ToLiteral());
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

    internal override void AddArgs(List<RedisValue> args)
    {
        args.Add(RedisLiterals.BYBOX);
        args.Add(_width);
        args.Add(_height);
        args.Add(Unit.ToLiteral());
    }
}
