
using System;

namespace StackExchange.Redis
{
    /// <summary>
    /// GeoRadius command options.
    /// </summary>
    [Flags]
    public enum GeoRadiusOptions
    {
        /// <summary>
        /// No Options
        /// </summary>
        None = 0,
        /// <summary>
        /// Redis will return the coordinates of any results.
        /// </summary>
        WithCoordinates = 1,
        /// <summary>
        /// Redis will return the distance from center for all results.
        /// </summary>
        WithDistance = 2,
        /// <summary>
        /// Redis will return the geo hash value as an integer. (This is the score in the sorted set)
        /// </summary>
        WithGeoHash = 4,
        /// <summary>
        /// Populates the commonly used values from the entry (the integer hash is not returned as it is not commonly useful)
        /// </summary>
        Default = WithCoordinates | GeoRadiusOptions.WithDistance
    }

    /// <summary>
    /// The result of a GeoRadius command.
    /// </summary>
    public struct GeoRadiusResult
    {
        /// <summary>
        /// Indicate the member being represented
        /// </summary>
        public override string ToString() => Member.ToString();
        /// <summary>
        /// The matched member.
        /// </summary>
        public RedisValue Member { get; }


        /// <summary>
        /// The distance of the matched member from the center of the geo radius command.
        /// </summary>
        public double? Distance { get; }

        /// <summary>
        /// The hash value of the matched member as an integer. (The key in the sorted set)
        /// </summary>
        /// <remarks>Note that this is not the same as the hash returned from GeoHash</remarks>
        public long? Hash { get; }

        /// <summary>
        /// The coordinates of the matched member.
        /// </summary>
        public GeoPosition? Position { get; }

        /// <summary>
        /// Returns a new GeoRadiusResult
        /// </summary>
        internal GeoRadiusResult(RedisValue member, double? distance, long? hash, GeoPosition? position)
        {
            Member = member;
            Distance = distance;
            Hash = hash;
            Position = position;
        }
    }

    /// <summary>
    /// Describes the longitude and latitude of a GeoEntry
    /// </summary>
    public struct GeoPosition : IEquatable<GeoPosition>
    {
        internal static string GetRedisUnit(GeoUnit unit)
        {
            switch (unit)
            {
                case GeoUnit.Meters: return "m";
                case GeoUnit.Kilometers: return "km";
                case GeoUnit.Miles: return "mi";
                case GeoUnit.Feet: return "ft";
                default:
                    throw new ArgumentOutOfRangeException(nameof(unit));
            }
        }

        /// <summary>
        /// The Latitude of the GeoPosition
        /// </summary>
        public double Latitude { get; }

        /// <summary>
        /// The Logitude of the GeoPosition
        /// </summary>
        public double Longitude { get; }

        /// <summary>
        /// Creates a new GeoPosition
        /// </summary>
        /// <param name="longitude"></param>
        /// <param name="latitude"></param>
        public GeoPosition(double longitude, double latitude)
        {
            Longitude = longitude;
            Latitude = latitude;
        }

        /// <summary>
        /// See Object.ToString()
        /// </summary>
        public override string ToString()
        {
            return string.Format("{0} {1}", Longitude, Latitude);
        }
        /// <summary>
        /// See Object.GetHashCode()
        /// Diagonals not an issue in the case of lat/long
        /// </summary>
        public override int GetHashCode()
        {
            // diagonals not an issue in the case of lat/long
            return Longitude.GetHashCode() ^ Latitude.GetHashCode();
        }
        /// <summary>
        /// Compares two values for equality
        /// </summary>
        public override bool Equals(object obj)
        {
            return obj is GeoPosition && Equals((GeoPosition)obj);
        }
        /// <summary>
        /// Compares two values for equality
        /// </summary>
        public bool Equals(GeoPosition value)
        {
            return this == value;
        }
        /// <summary>
        /// Compares two values for equality
        /// </summary>
        public static bool operator ==(GeoPosition x, GeoPosition y)
        {
            return x.Longitude == y.Longitude && x.Latitude == y.Latitude;
        }
        /// <summary>
        /// Compares two values for non-equality
        /// </summary>
        public static bool operator !=(GeoPosition x, GeoPosition y)
        {
            return x.Longitude != y.Longitude || x.Latitude != y.Latitude;
        }
    }

    /// <summary>
    /// Describes a GeoEntry element with the corresponding value
    /// GeoEntries are stored in redis as SortedSetEntries
    /// </summary>
    public struct GeoEntry : IEquatable<GeoEntry>
    {
        /// <summary>
        /// The name of the geo entry
        /// </summary>
        public RedisValue Member { get; }

        /// <summary>
        /// Describes the longitude and latitude of a GeoEntry
        /// </summary>
        public GeoPosition Position { get; }

        /// <summary>
        /// Initializes a GeoEntry value
        /// </summary>
        public GeoEntry(double longitude, double latitude, RedisValue member)
        {
            Member = member;
            Position = new GeoPosition(longitude, latitude);
        }



        /// <summary>
        /// The longitude of the geo entry
        /// </summary>
        public double Longitude => Position.Longitude;

        /// <summary>
        /// The latitude of the geo entry
        /// </summary>
        public double Latitude => Position.Latitude;

        /// <summary>
        /// See Object.ToString()
        /// </summary>
        public override string ToString()
        {
            return $"({Longitude},{Latitude})={Member}";
        }
        /// <summary>
        /// See Object.GetHashCode()
        /// </summary>
        public override int GetHashCode()
        {
            return Position.GetHashCode() ^ Member.GetHashCode();
        }
        /// <summary>
        /// Compares two values for equality
        /// </summary>
        public override bool Equals(object obj)
        {
            return obj is GeoEntry && Equals((GeoEntry)obj);
        }
        /// <summary>
        /// Compares two values for equality
        /// </summary>
        public bool Equals(GeoEntry value)
        {
            return this == value;
        }
        /// <summary>
        /// Compares two values for equality
        /// </summary>
        public static bool operator ==(GeoEntry x, GeoEntry y)
        {
            return x.Position == y.Position && x.Member == y.Member;
        }
        /// <summary>
        /// Compares two values for non-equality
        /// </summary>
        public static bool operator !=(GeoEntry x, GeoEntry y)
        {
            return x.Position != y.Position || x.Member != y.Member;
        }
    }
}