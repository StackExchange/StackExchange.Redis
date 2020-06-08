// .NET port of https://github.com/RedisLabs/JRediSearch/

using System;
using System.Globalization;
using StackExchange.Redis;

namespace NRediSearch
{
    public static class Extensions
    {
        internal static string AsRedisString(this double value, bool forceDecimal = false)
        {
            if (double.IsNegativeInfinity(value))
            {
                return "-inf";
            }
            else if (double.IsPositiveInfinity(value))
            {
                return "inf";
            }
            else
            {
                return value.ToString(forceDecimal ? "#.0" : "G17", NumberFormatInfo.InvariantInfo);
            }
        }
        internal static string AsRedisString(this GeoUnit value)
        {
            return value switch
            {
                GeoUnit.Feet => "ft",
                GeoUnit.Kilometers => "km",
                GeoUnit.Meters => "m",
                GeoUnit.Miles => "mi",
                _ => throw new InvalidOperationException($"Unknown unit: {value}"),
            };
        }
    }
}
