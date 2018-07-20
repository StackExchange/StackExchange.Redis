using System;
using System.Net;
using StackExchange.Redis;
using StackExchange.Redis.Tests;

namespace TestConsole
{
    internal static class Program
    {
        private static void Main()
        {
            var ep = new IPEndPoint(IPAddress.Loopback, 6378);
            using (var server = new FakeRedisServer(Console.Out))
            {
                server.Start(ep);

                var cfg = new ConfigurationOptions { EndPoints = { ep } };
                using (var client = ConnectionMultiplexer.Connect(cfg))
                {

                    Console.ReadLine();
                }
            }
        }
    }
}
