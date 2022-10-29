using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using StackExchange.Redis.Server;

namespace KestrelRedisServer
{
    public class RedisConnectionHandler : ConnectionHandler
    {
        private readonly RespServer _server;
        public RedisConnectionHandler(RespServer server) => _server = server;
        public override Task OnConnectedAsync(ConnectionContext connection)
            => _server.RunClientAsync(connection.Transport);
    }
}
