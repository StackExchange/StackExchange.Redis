using StackExchange.Redis;
using System.Collections;
namespace NRediSearch
{
    /// <summary>
    /// Cache to ensure we encode and box literals once only
    /// </summary>
    internal static class Literals
    {
        private static Hashtable _boxed = new Hashtable();
        private static object _null = RedisValue.Null;
        /// <summary>
        /// Obtain a lazily-cached pre-encoded and boxed representation of a string
        /// </summary>
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
    }
}
