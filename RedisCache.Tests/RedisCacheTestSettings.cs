namespace Saxo.RedisCache.Tests
{
    class RedisCacheTestSettings : IRedisCacheSettings
    {
        public string ServerAddress
        {
            get { return "localhost"; }
        }
    }
}
