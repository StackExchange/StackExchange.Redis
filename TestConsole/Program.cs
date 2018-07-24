using System;
using System.Net;
using System.Threading.Tasks;
using StackExchange.Redis.Server;

static class Program
{
    static async Task Main()
    {
        using (var resp = new MemoryCacheRedisServer(Console.Out))
        using (var socket = new RespSocketServer(resp))
        {
            socket.Listen(new IPEndPoint(IPAddress.Loopback, 6378));
            await resp.Shutdown;
        }
    }
}
