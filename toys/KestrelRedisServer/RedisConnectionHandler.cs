using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Libuv.Internal.Networking;
using StackExchange.Redis.Server;

namespace KestrelRedisServer
{
    public class RedisConnectionHandler : ConnectionHandler
    {
        private readonly RespServer _server;
        public RedisConnectionHandler(RespServer server) => _server = server;
        public override async Task OnConnectedAsync(ConnectionContext connection)
        {
            try
            {
                await _server.RunClientAsync(connection.Transport).ConfigureAwait(false);
            }
            catch (IOException io) when (io.InnerException is UvException uv && uv.StatusCode == -4077)
            { } //swallow libuv disconnect
        }
    }
}
