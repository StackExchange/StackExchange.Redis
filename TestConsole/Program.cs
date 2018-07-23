using System;
using System.Net;
using System.Threading.Tasks;
using StackExchange.Redis.Server;

internal static class Program
{
    private static async Task Main()
    {
        using (var server = new MemoryCacheServer(Console.Out))
        {
            server.Listen(new IPEndPoint(IPAddress.Loopback, 6378));
            await server.Shutdown;
        }
    }
}
