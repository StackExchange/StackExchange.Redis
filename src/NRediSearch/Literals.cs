// .NET port of https://github.com/RedisLabs/JRediSearch/

using StackExchange.Redis;
using System.Collections;
using System.Linq;

namespace NRediSearch
{
    /// <summary>
    /// Cache to ensure we encode and box literals once only
    /// </summary>
    internal static class Literals
    {
        private static readonly Hashtable _boxed = new Hashtable();
        private static readonly object _null = RedisValue.Null;
        /// <summary>
        /// Obtain a lazily-cached pre-encoded and boxed representation of a string
        /// </summary>
        /// <param name="value">The value to get a literal representation for.</param>
        /// <remarks>This shoul donly be used for fixed values, not user data (the cache is never reclaimed, so it will be a memory leak)</remarks>
        public static object Literal(this string value)
        {
            if (value == null) return _null;

            object boxed = _boxed[value];
            if (boxed == null)
            {
                lock (_boxed)
                {
                    boxed = _boxed[value];
                    if (boxed == null)
                    {
                        boxed = (RedisValue)value;
                        _boxed.Add(value, boxed);
                    }
                }
            }
            return boxed;
        }

        private const int BOXED_MIN = -1, BOXED_MAX = 20;
        private static readonly object[] s_Boxed = Enumerable.Range(BOXED_MIN, BOXED_MAX - BOXED_MIN).Select(i => (object)i).ToArray();

        /// <summary>
        /// Obtain a pre-boxed integer if possible, else box the inbound value
        /// </summary>
        /// <param name="value">The value to get a pre-boxed integer for.</param>
        public static object Boxed(this int value) => value >= BOXED_MIN && value < BOXED_MAX ? s_Boxed[value - BOXED_MIN] : value;
    }
}
