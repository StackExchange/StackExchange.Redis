using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RedisCache;

namespace RedisCache.Tests
{
    class RedisCacheTestSettings : IRedisCacheSettings
    {
        public string ServerAddress
        {
            get { return "localhost"; }
        }
    }
}
