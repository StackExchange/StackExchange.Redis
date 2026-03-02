using System.Diagnostics;
using Microsoft.AspNetCore.Connections;
using StackExchange.Redis.Server;

namespace KestrelRedisServer
{
    public class RedisConnectionHandler(RedisServer server) : ConnectionHandler
    {
        public override Task OnConnectedAsync(ConnectionContext connection)
        {
            RedisServer.Node? node;
            if (!(connection.LocalEndPoint is { } ep && server.TryGetNode(ep, out node)))
            {
                node = null;
            }

            return server.RunClientAsync(connection.Transport, node: node)
            .ContinueWith(t =>
            {
                // ensure any exceptions are observed
                var ex = t.Exception;
                if (ex != null)
                {
                    Debug.WriteLine(ex.Message);
                    GC.KeepAlive(ex);
                }
            },
            TaskContinuationOptions.OnlyOnFaulted);
        }
    }
}
