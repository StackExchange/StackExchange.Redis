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
            return server.RunClientAsync(connection.Transport, node: node);
        }
    }
}
