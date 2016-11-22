using System;
using System.ComponentModel;

namespace StackExchange.Redis
{
    /// <summary>
    /// Units associated with Geo Commands
    /// </summary>
    public enum GeoUnit
    {
        /// <summary>
        /// Meters/Metres
        /// </summary>
        Meters,
        /// <summary>
        /// Kilometers/Kilometres
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