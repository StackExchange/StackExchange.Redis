// .NET port of https://github.com/RedisLabs/JRediSearch/

using System.Text;
using StackExchange.Redis;

namespace NRediSearch.QueryBuilder
{
    public class GeoValue : Value
    {
        private readonly GeoUnit _unit;
        private readonly double _lon, _lat, _radius;

        public GeoValue(double lon, double lat, double radius, GeoUnit unit)
        {
            _lon = lon;
            _lat = lat;
            _radius = radius;
            _unit = unit;
        }

        public override string ToString()
        {
            return new StringBuilder("[")
                    .Append(_lon.AsRedisString(true)).Append(" ")
                    .Append(_lat.AsRedisString(true)).Append(" ")
                    .Append(_radius.AsRedisString(true)).Append(" ")
                    .Append(_unit.AsRedisString())
                    .Append("]").ToString();
        }

        public override bool IsCombinable() => false;
    }
}
