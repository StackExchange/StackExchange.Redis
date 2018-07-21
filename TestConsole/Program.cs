using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using StackExchange.Redis;
using StackExchange.Redis.Server;
using StackExchange.Redis.Tests;

namespace TestConsole
{
    internal static class Program
    {
        class FakeRedisServer : BasicRedisServer
        {
            public FakeRedisServer(TextWriter output = null) : base(output) { }
            public override int Databases => 1;
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
                    for(int i = 0; i < LOOP; i++)
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
