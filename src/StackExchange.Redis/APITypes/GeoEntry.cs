using System;

namespace StackExchange.Redis;

/// <summary>
/// Describes a GeoEntry element with the corresponding value.
/// GeoEntries are stored in redis as SortedSetEntries.
/// </summary>
public readonly struct GeoEntry : IEquatable<GeoEntry>
{
    /// <summary>
    /// The name of the GeoEntry.
    /// </summary>
    public RedisValue Member { get; }

    /// <summary>
    /// Describes the longitude and latitude of a GeoEntry.
    /// </summary>
    public GeoPosition Position { get; }

    /// <summary>
    /// Initializes a GeoEntry value.
    /// </summary>
    /// <param name="longitude">The longitude position to use.</param>
    /// <param name="latitude">The latitude position to use.</param>
    /// <param name="member">The value to store for this position.</param>
    public GeoEntry(double longitude, double latitude, RedisValue member)
    {
        Member = member;
        Position = new GeoPosition(longitude, latitude);
    }

    /// <summary>
    /// The longitude of the GeoEntry.
    /// </summary>
    public double Longitude => Position.Longitude;

    /// <summary>
    /// The latitude of the GeoEntry.
    /// </summary>
    public double Latitude => Position.Latitude;

    /// <summary>
    /// A "({Longitude},{Latitude})={Member}" string representation of this entry.
    /// </summary>
    public override string ToString() => $"({Longitude},{Latitude})={Member}";

    /// <inheritdoc/>
    public override int GetHashCode() => Position.GetHashCode() ^ Member.GetHashCode();

    /// <summary>
    /// Compares two values for equality.
    /// </summary>
    /// <param name="obj">The <see cref="GeoEntry"/> to compare to.</param>
    public override bool Equals(object? obj) => obj is GeoEntry geObj && Equals(geObj);

    /// <summary>
    /// Compares two values for equality.
    /// </summary>
    /// <param name="other">The <see cref="GeoEntry"/> to compare to.</param>
    public bool Equals(GeoEntry other) => this == other;

    /// <summary>
    /// Compares two values for equality.
    /// </summary>
    /// <param name="x">The first entry to compare.</param>
    /// <param name="y">The second entry to compare.</param>
    public static bool operator ==(GeoEntry x, GeoEntry y) => x.Position == y.Position && x.Member == y.Member;

    /// <summary>
    /// Compares two values for non-equality.
    /// </summary>
    /// <param name="x">The first entry to compare.</param>
    /// <param name="y">The second entry to compare.</param>
    public static bool operator !=(GeoEntry x, GeoEntry y) => x.Position != y.Position || x.Member != y.Member;
}
