using StackExchange.Redis.Protocol;

namespace StackExchange.Redis;

/// <summary>
/// A Shape that you can use for a GeoSearch.
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
    /// constructs a <see cref="GeoSearchShape"/>.
    /// </summary>
    /// <param name="unit">The geography unit to use.</param>
    public GeoSearchShape(GeoUnit unit)
    {
        Unit = unit;
    }

    /// <summary>Write the shape straight to a frame, for the interpolated surface.</summary>
    /// <remarks>
    /// Internal abstract, so the type is effectively sealed to this assembly.
    /// </remarks>
    /// <param name="command">The frame being written.</param>
    internal abstract void AddArgs(ref RespRequestBuilder command);
}

/// <summary>
/// A circle drawn on a map bounding.
/// </summary>
public class GeoSearchCircle : GeoSearchShape
{
    private readonly double _radius;

    /// <summary>
    /// Creates a <see cref="GeoSearchCircle"/> Shape.
    /// </summary>
    /// <param name="radius">The radius of the circle.</param>
    /// <param name="unit">The distance unit the circle will use, defaults to Meters.</param>
    public GeoSearchCircle(double radius, GeoUnit unit = GeoUnit.Meters) : base(unit)
    {
        _radius = radius;
    }

    internal sealed override int ArgCount => 3;

    /// <inheritdoc/>
    internal sealed override void AddArgs(ref RespRequestBuilder command)
    {
        command.AppendFormatted(RespLiterals.ByRadius);
        command.AppendFormatted((RedisValue)_radius);
        command.AppendFormatted(Unit.ToLiteral());
    }
}

/// <summary>
/// A box drawn on a map.
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

    internal sealed override int ArgCount => 4;

    /// <inheritdoc/>
    internal sealed override void AddArgs(ref RespRequestBuilder command)
    {
        command.AppendFormatted(RespLiterals.ByBox);
        command.AppendFormatted((RedisValue)_width);
        command.AppendFormatted((RedisValue)_height);
        command.AppendFormatted(Unit.ToLiteral());
    }
}
