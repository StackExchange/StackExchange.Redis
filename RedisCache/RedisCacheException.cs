using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RedisCache
{
    public class RedisCacheException : Exception
    {
        public RedisCacheException(string message) : base(message)
        {
        }
    }
}
