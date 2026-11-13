using System;

namespace StackExchange.Redis;

/// <summary>
/// Where a geospatial search starts: a member of the set (<c>FROMMEMBER</c>) or a longitude/latitude
/// position (<c>FROMLONLAT</c>). One operand replaces the per-origin method pairs the old surface
/// accumulated: the origin is data, and <see cref="Geospatial.SearchAsync"/> and
/// <see cref="Geospatial.SearchAndStoreAsync"/> each take the same one.
/// </summary>
/// <remarks>
/// Rarely written explicitly: a <see cref="RedisValue"/> or <see langword="string"/> member and a
/// <see cref="GeoPosition"/> each convert implicitly. (Not a tuple: this library deliberately never
/// references <c>System.ValueTuple</c>, which down-level targets only get from an extra package.)
/// </remarks>
public readonly struct GeoCenter
{
    private enum CenterKind : byte
    {
        None = 0, // the default instance, which no search accepts
        Member = 1,
        Position = 2,
    }

    private readonly CenterKind _kind;
    private readonly RedisValue _member;
    private readonly double _longitude;
    private readonly double _latitude;

    private GeoCenter(CenterKind kind, in RedisValue member, double longitude, double latitude)
    {
        _kind = kind;
        _member = member;
        _longitude = longitude;
        _latitude = latitude;
    }

    /// <summary>Search around an existing member of the set: <c>FROMMEMBER</c>.</summary>
    /// <param name="member">The member to search around; must not be null.</param>
    public static GeoCenter Member(RedisValue member)
        => member.IsNull
            ? throw new ArgumentNullException(nameof(member))
            : new(CenterKind.Member, member, double.NaN, double.NaN);

    /// <summary>Search around a coordinate: <c>FROMLONLAT</c>.</summary>
    /// <param name="longitude">The longitude to search around.</param>
    /// <param name="latitude">The latitude to search around.</param>
    public static GeoCenter Position(double longitude, double latitude)
        => new(CenterKind.Position, RedisValue.Null, longitude, latitude);

    /// <summary>Search around an existing member of the set.</summary>
    /// <param name="member">The member to search around.</param>
    public static implicit operator GeoCenter(RedisValue member) => Member(member);

    /// <summary>Search around an existing member of the set.</summary>
    /// <param name="member">The member to search around.</param>
    public static implicit operator GeoCenter(string member) => Member(member);

    /// <summary>Search around a coordinate.</summary>
    /// <param name="position">The position to search around.</param>
    public static implicit operator GeoCenter(GeoPosition position) => Position(position.Longitude, position.Latitude);

    // the sentinels SearchCore has always used: a null member means "by position", NaN means "no position"
    internal RedisValue MemberOrNull => _kind == CenterKind.Member ? _member : RedisValue.Null;

    internal double Longitude => _kind == CenterKind.Position ? _longitude : double.NaN;

    internal double Latitude => _kind == CenterKind.Position ? _latitude : double.NaN;

    internal void Validate(string paramName)
    {
        if (_kind == CenterKind.None)
        {
            throw new ArgumentException("A search center is required: a member of the set, or a longitude/latitude position.", paramName);
        }
    }

    /// <inheritdoc/>
    public override string ToString() => _kind switch
    {
        CenterKind.Member => $"member {_member}",
        CenterKind.Position => $"{_longitude},{_latitude}",
        _ => "(none)",
    };
}
