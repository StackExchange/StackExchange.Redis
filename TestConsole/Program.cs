using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using StackExchange.Redis;
using StackExchange.Redis.Server;
using System.Runtime.Caching;
using System.Threading;
using System.Threading.Tasks;

namespace TestConsole
{
    internal static class Program
    {
        class FakeRedisServer : BasicRedisServer
        {
            public FakeRedisServer(TextWriter output = null, MemoryCache cache = null) : base(1, output)
                => _cache = cache ?? MemoryCache.Default;

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
        private static async Task Main()
        {
            long oldOps = 0;
            var ep = new IPEndPoint(IPAddress.Loopback, 6378);
            using (var server = new FakeRedisServer(Console.Out))
            using (var timer = new Timer(_ =>
            {
                var ops = server.CommandsProcesed;
                if(oldOps != ops)
                {
                    lock(Console.Out)
                    {
                        Console.WriteLine($"Commands processed: " + ops);
                    }
                    oldOps = ops;
                }
            }, null, 1000, 1000))
            {
                server.Listen(ep);
                TimeClient(ep);
                await server.Shutdown;
            }
        }

        static void TimeClient(EndPoint ep)
        {
            Console.WriteLine("testing server...");
            var cfg = new ConfigurationOptions { EndPoints = { ep } };
            using (var client = ConnectionMultiplexer.Connect(cfg))
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
        }
    }
}
