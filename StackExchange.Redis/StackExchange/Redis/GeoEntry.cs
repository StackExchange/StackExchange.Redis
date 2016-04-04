
using System;
using System.Collections.Generic;
using System.Runtime.Remoting.Metadata.W3cXsd2001;

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
        WITHCOORD = 1,
        /// <summary>
        /// Redis will return the distance from center for all results.
        /// </summary>
        WITHDIST = 2,
        /// <summary>
        /// Redis will return the geo hash value as an integer. (This is the score in the sorted set)
        /// </summary>
        WITHHASH = 4
    }

    /// <summary>
    /// The result of a GeoRadius command.
    /// </summary>
    public struct GeoRadiusResult
    {
        internal readonly RedisValue member;
        internal readonly GeoRadius geoRadius;
        internal readonly double? distanceFromCenter;
        internal readonly long? geoHash;
        internal readonly GeoPosition? geoPosition;

        /// <summary>
        /// The matched member.
        /// </summary>
        public RedisValue Member => member;
        /// <summary>
        /// The original GeoRadius command
        /// </summary>
        public GeoRadius GeoRadius => geoRadius;
        /// <summary>
        /// The distance of the matched member from the center of the geo radius command.
        /// </summary>
        public double? DistanceFromCenter => distanceFromCenter;
        /// <summary>
        /// The Geo Hash of the matched member as an integer. (The key in the sorted set)
        /// </summary>
        public long? GeoHash => geoHash;
        /// <summary>
        /// The coordinates of the matched member.
        /// </summary>
        public GeoPosition? GeoPosition => geoPosition;

        /// <summary>
        /// Returns a new GeoRadiusResult
        /// </summary>
        public GeoRadiusResult(RedisValue member,GeoRadius geoRadius,double? distanceFromCenter,long? geoHash,GeoPosition? geoPosition)
        {
            this.member = member;
            this.geoRadius = geoRadius;
            this.distanceFromCenter = distanceFromCenter;
            this.geoHash = geoHash;
            this.geoPosition = geoPosition;
        }

    }

    /// <summary>
    /// Represents a GeoRadius command and its options.
    /// </summary>
    public class GeoRadius
    {
        internal readonly GeoUnit geoUnit;
        internal readonly GeoRadiusOptions geoRadiusOptions ;
        internal readonly int maxReturnCount;
        internal readonly RedisKey key;
        internal readonly GeoPosition geoPosition;
        internal readonly double radius;

        /// <summary>
        /// The Radius size of this GeoRadius command.
        /// </summary>
        public double Radius => radius;
        /// <summary>
        /// The center point to base the search.
        /// </summary>
        public GeoPosition GeoPosition => geoPosition;
        /// <summary>
        /// The key to use.
        /// </summary>
        public RedisKey Key => key;
        /// <summary>
        /// The unit to return distance measurments in.
        /// </summary>
        public GeoUnit GeoUnit => geoUnit;
        /// <summary>
        /// The possible options for the GeoRadius command
        /// </summary>
        public GeoRadiusOptions GeoRadiusOptions => geoRadiusOptions;
        /// <summary>
        /// The maximum number of results to return.
        /// However note that internally the command needs to perform an effort proportional to the number of items matching the specified area, so to query very large areas with a very small COUNT option may be slow even if just a few results are returned.
        /// </summary>
        public int MaxReturnCount => maxReturnCount;
        /// <summary>
        /// Creates a new GeoRadius
        /// </summary>
        public GeoRadius(RedisKey key,GeoPosition geoPosition,double radius,int maxReturnCount =-1,GeoUnit geoUnit = GeoUnit.Meters,GeoRadiusOptions geoRadiusOptions = (GeoRadiusOptions.WITHCOORD | GeoRadiusOptions.WITHDIST | GeoRadiusOptions.WITHHASH))
        {
            this.key = key;
            this.geoPosition = geoPosition;
            this.radius = radius;
            this.geoUnit = geoUnit;
            this.geoRadiusOptions = geoRadiusOptions;
            this.maxReturnCount = maxReturnCount;
        }

    }

    /// <summary>
    /// Describes the longitude and latitude of a GeoEntry
    /// </summary>
    public struct GeoPosition
    {
        internal readonly double longitude;
        internal readonly double latitude;

        /// <summary>
        /// The Latitude of the GeoPosition
        /// </summary>
        public double Latitude => latitude;
        /// <summary>
        /// The Logitude of the GeoPosition
        /// </summary>
        public double Longitude => longitude;

        /// <summary>
        /// Creates a new GeoPosition
        /// </summary>
        /// <param name="longitude"></param>
        /// <param name="latitude"></param>
        public GeoPosition(double longitude,double latitude)
        {
            this.longitude = longitude;
            this.latitude = latitude;
        }

        /// <summary>
        /// See Object.ToString()
        /// </summary>
        public override string ToString()
        {
            return string.Format("{0} {1}", longitude, latitude);
        }
        /// <summary>
        /// See Object.GetHashCode()
        /// </summary>
        public override int GetHashCode()
        {
            return longitude.GetHashCode() ^ latitude.GetHashCode();
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
            return Equals(longitude, value.Longitude) && Equals(latitude, value.latitude);
        }
        /// <summary>
        /// Compares two values for equality
        /// </summary>
        public static bool operator ==(GeoPosition x, GeoPosition y)
        {
            return Equals(x.longitude, y.longitude) && Equals(x.latitude, y.latitude);
        }
        /// <summary>
        /// Compares two values for non-equality
        /// </summary>
        public static bool operator !=(GeoPosition x, GeoPosition y)
        {
            return !Equals(x.longitude, y.longitude) || !Equals(x.latitude, y.latitude);
        }
    }

    /// <summary>
    /// Describes a GeoEntry element with the corresponding value
    /// GeoEntries are stored in redis as SortedSetEntries
    /// </summary>
    public struct GeoEntry : IEquatable<GeoEntry>
    {
        internal readonly RedisValue member;
        internal readonly GeoPosition geoPos;

        /// <summary>
        /// Initializes a GeoEntry value
        /// </summary>
        public GeoEntry(double longitude,double latitude,RedisValue member)
        {
            this.member = member;
            geoPos = new GeoPosition(longitude, latitude);
        }

        /// <summary>
        /// The name of the geo entry
        /// </summary>
        public string Member => member;

        /// <summary>
        /// The longitude of the geo entry
        /// </summary>
        public double Longitude => geoPos.Longitude;

        /// <summary>
        /// The latitude of the geo entry
        /// </summary>
        public double Latitude => geoPos.Latitude;

        /// <summary>
        /// Describes the longitude and latitude of a GeoEntry
        /// </summary>
        public GeoPosition GeoPosition => geoPos;
       

        /// <summary>
        /// See Object.ToString()
        /// </summary>
        public override string ToString()
       {
           return string.Format("{0} {1} {2}", geoPos.Latitude, geoPos.Latitude, member);
       }
        /// <summary>
        /// See Object.GetHashCode()
        /// </summary>
        public override int GetHashCode()
        {
            return geoPos.GetHashCode() ^ member.GetHashCode();
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
            return Equals(geoPos, value.geoPos) && member == value.member;
        }
        /// <summary>
        /// Compares two values for equality
        /// </summary>
        public static bool operator ==(GeoEntry x, GeoEntry y)
        {
            return Equals(x.geoPos,y.geoPos) && x.member == y.member;
        }
        /// <summary>
        /// Compares two values for non-equality
        /// </summary>
        public static bool operator !=(GeoEntry x, GeoEntry y)
        {
            return !Equals(x.geoPos , y.geoPos) || x.member != y.member;
        }
    }
}