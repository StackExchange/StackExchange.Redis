using System;
using System.Net;
using System.Threading.Tasks;
using StackExchange.Redis.Server;

static class Program
{
    static async Task Main()
    {
        using (var server = new MemoryCacheRedisServer(Console.Out))
        {
            server.Listen(new IPEndPoint(IPAddress.Loopback, 6378));
            await server.Shutdown;
        }
    }
}
