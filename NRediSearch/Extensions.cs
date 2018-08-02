// .NET port of https://github.com/RedisLabs/JRediSearch/

using System;
using System.Globalization;
using StackExchange.Redis;
using static NRediSearch.Client;

namespace NRediSearch
{
    public static class Extensions
    {
        /// <summary>
        /// Set a custom stopword list
        /// </summary>
        /// <param name="options">The <see cref="IndexOptions"/> to set stopwords on.</param>
        /// <param name="stopwords">The stopwords to set.</param>
        public static ConfiguredIndexOptions SetStopwords(this IndexOptions options, params string[] stopwords)
            => new ConfiguredIndexOptions(options).SetStopwords(stopwords);

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
            switch (value)
            {
                case GeoUnit.Feet: return "ft";
                case GeoUnit.Kilometers: return "km";
                case GeoUnit.Meters: return "m";
                case GeoUnit.Miles: return "mi";
                default: throw new InvalidOperationException($"Unknown unit: {value}");
            }
        }
    }
}
