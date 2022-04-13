using System;

namespace StackExchange.Redis
{
    /// <summary>
    /// Units associated with Geo Commands.
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
        Feet,
    }

    internal static class GeoUnitExtensions
    {
        internal static RedisValue ToLiteral(this GeoUnit unit) => unit switch
        {
            GeoUnit.Feet => RedisLiterals.ft,
            GeoUnit.Kilometers => RedisLiterals.km,
            GeoUnit.Meters => RedisLiterals.m,
            GeoUnit.Miles => RedisLiterals.mi,
            _ => throw new ArgumentOutOfRangeException(nameof(unit))
        };
    }
}
