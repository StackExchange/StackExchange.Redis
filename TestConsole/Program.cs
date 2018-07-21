using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using StackExchange.Redis;
using StackExchange.Redis.Server;
using System.Runtime.Caching;

namespace TestConsole
{
    internal static class Program
    {
        class FakeRedisServer : BasicRedisServer
        {
            public FakeRedisServer(TextWriter output = null, MemoryCache cache = null) : base(output)
                => _cache = cache ?? MemoryCache.Default;
            public override int Databases => 1;

            private readonly MemoryCache _cache;

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _cache.Dispose();
                }
                base.Dispose(disposing);
            }

            protected override long Dbsize(int database) => _cache.GetCount();
            protected override RedisValue Get(int database, RedisKey key)
            {
                var val = _cache[key];
                if (val == null) return RedisValue.Null;
                return (RedisValue)val;
            }
            protected override void Set(int database, RedisKey key, RedisValue value)
                => _cache[key] = value;
            protected override bool Del(int database, RedisKey key)
                => _cache.Remove(key) != null;

        }
        private static void Main()
        {
            var ep = new IPEndPoint(IPAddress.Loopback, 6378);
            using (var server = new FakeRedisServer(Console.Out))
            {
                server.Listen(ep);
                Console.WriteLine($"Server running on {ep}; press return to connect as client");
                Console.ReadLine();
                var cfg = new ConfigurationOptions { EndPoints = { ep } };
                using (var client = ConnectionMultiplexer.Connect(cfg, Console.Out))
                {
                    var db = client.GetDatabase();
                    var watch = Stopwatch.StartNew();
                    const int LOOP = 1000;
                    for (int i = 0; i < LOOP; i++)
                    {
                        db.Ping();
                    }
                    watch.Stop();

                    Console.WriteLine($"ping {LOOP} times: {watch.ElapsedMilliseconds}ms");
                }
                Console.ReadLine();
            }
        }
    }
}
