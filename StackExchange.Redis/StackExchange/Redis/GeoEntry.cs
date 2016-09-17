
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
        WithGeoHash = 4
    }

    /// <summary>
    /// The result of a GeoRadius command.
    /// </summary>
    public struct GeoRadiusResult
    {
        /// <summary>
        /// The matched member.
        /// </summary>
        public RedisValue Member { get; }

        /// <summary>
        /// The original GeoRadius command
        /// </summary>
        public GeoRadius Command { get; }

        /// <summary>
        /// The distance of the matched member from the center of the geo radius command.
        /// </summary>
        public double? DistanceFromCenter { get; }

        /// <summary>
        /// The Geo Hash of the matched member as an integer. (The key in the sorted set)
        /// </summary>
        public long? GeoHash { get; }

        /// <summary>
        /// The coordinates of the matched member.
        /// </summary>
        public GeoPosition? GeoPosition { get; }

        /// <summary>
        /// Returns a new GeoRadiusResult
        /// </summary>
        public GeoRadiusResult(RedisValue member,GeoRadius command,double? distanceFromCenter,long? geoHash,GeoPosition? geoPosition)
        {
            Member = member;
            Command = command;
            DistanceFromCenter = distanceFromCenter;
            GeoHash = geoHash;
            GeoPosition = geoPosition;
        }

      

    }

    /// <summary>
    /// Represents a GeoRadius command and its options.
    /// </summary>
    public class GeoRadius
    {
        /// <summary>
        /// The Radius size of this GeoRadius command.
        /// </summary>
        public double Radius { get; }

        /// <summary>
        /// The center point to base the search.
        /// </summary>
        public GeoPosition GeoPosition { get; }

        /// <summary>
        /// The key to use.
        /// </summary>
        public RedisKey Key { get; }

        /// <summary>
        /// The unit to return distance measurments in.
        /// </summary>
        public GeoUnit Unit { get; }

        /// <summary>
        /// The possible options for the GeoRadius command
        /// </summary>
        public GeoRadiusOptions GeoRadiusOptions { get; }

        /// <summary>
        /// The maximum number of results to return.
        /// However note that internally the command needs to perform an effort proportional to the number of items matching the specified area, so to query very large areas with a very small COUNT option may be slow even if just a few results are returned.
        /// </summary>
        public int MaxReturnCount { get; }

        /// <summary>
        /// Creates a new GeoRadius
        /// </summary>
        public GeoRadius(RedisKey key,GeoPosition geoPosition,double radius,int maxReturnCount =-1,GeoUnit unit = GeoUnit.Meters,GeoRadiusOptions geoRadiusOptions = (GeoRadiusOptions.WithCoordinates | GeoRadiusOptions.WithDistance | GeoRadiusOptions.WithGeoHash))
        {
            Key = key;
            GeoPosition = geoPosition;
            Radius = radius;
            Unit = unit;
            GeoRadiusOptions = geoRadiusOptions;
            MaxReturnCount = maxReturnCount;
        }

        public bool HasFlag(GeoRadiusOptions flag)
        {
            return (GeoRadiusOptions & flag) != 0;
        }
    }

    /// <summary>
    /// Describes the longitude and latitude of a GeoEntry
    /// </summary>
    public struct GeoPosition : IEquatable<GeoPosition>
    {
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
        public GeoPosition(double longitude,double latitude)
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
            return x.Longitude!= y.Longitude || x.Latitude != y.Latitude;
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
        public string Member { get; }

        /// <summary>
        /// Describes the longitude and latitude of a GeoEntry
        /// </summary>
        public GeoPosition Point { get; }

        /// <summary>
        /// Initializes a GeoEntry value
        /// </summary>
        public GeoEntry(double longitude,double latitude,RedisValue member)
        {
            Member = member;
            Point = new GeoPosition(longitude, latitude);
        }

       

        /// <summary>
        /// The longitude of the geo entry
        /// </summary>
        public double Longitude => Point.Longitude;

        /// <summary>
        /// The latitude of the geo entry
        /// </summary>
        public double Latitude => Point.Latitude;

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
            return Point.GetHashCode() ^ Member.GetHashCode();
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
            return Equals(x.Point, y.Point) && x.Member == y.Member;
        }
        /// <summary>
        /// Compares two values for non-equality
        /// </summary>
        public static bool operator !=(GeoEntry x, GeoEntry y)
        {
            return !Equals(x.Point, y.Point) || x.Member != y.Member;
        }
    }

    /// <summary>
    /// Units associated with Geo Commands
    /// </summary>
    public enum GeoUnit
    {
        /// <summary>
        /// Meters
        /// </summary>
        Meters,
        /// <summary>
        /// Kilometers
        /// </summary>
        Kilometers,
        /// <summary>
        /// Miles
        /// </summary>
        Miles,
        /// <summary>
        /// Feet
        /// </summary>
        Feet
    }


   

}