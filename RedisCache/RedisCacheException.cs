using System;

namespace Saxo.RedisCache
{
    public class RedisCacheException : Exception
    {
        public RedisCacheException(string message) : base(message)
        {
        }
    }
}
