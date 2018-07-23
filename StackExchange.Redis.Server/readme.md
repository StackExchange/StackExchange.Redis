# Wait, what is this?

This is **not** a replacement for redis!

This is some example code that illustrates using "pipelines" to implement a server, in this case a server that works like 'redis',
implementing the same protocol, and offering similar services.

What it isn't:

- supported
- as good as redis
- feature complete
- bug free

What it is:

- useful for me to test my protocol handling
- useful for debugging
- useful for anyone looking for reference code for implementing a custom server based on pipelines
- fun

Example usage:

```c#
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
            server.Listen(new IPEndPoint(IPAddress.Loopback, 6379));
            await server.Shutdown;
        }
    }
}
```