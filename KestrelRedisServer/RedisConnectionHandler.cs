using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.Logging;
using StackExchange.Redis.Server;

namespace KestrelRedisServer
{
    public class RedisConnectionHandler : ConnectionHandler
    {
        private readonly MemoryCacheRedisServer _server;
        public RedisConnectionHandler(ILogger<RedisConnectionHandler> logger)
        {
            _server = new MemoryCacheRedisServer();
        }
        public override async Task OnConnectedAsync(ConnectionContext connection)
        {
            var client = _server.AddClient();
            try
            {
                while (true)
                {
                    var read = await connection.Transport.Input.ReadAsync();
                    var buffer = read.Buffer;
                    bool makingProgress = false;
                    while (_server.TryProcessRequest(ref buffer, client, connection.Transport.Output))
                    {
                        makingProgress = true;
                        await connection.Transport.Output.FlushAsync();
                    }
                    connection.Transport.Input.AdvanceTo(buffer.Start, buffer.End);

                    if (!makingProgress && read.IsCompleted) break;
                }
            }
            finally
            {
                _server.RemoveClient(client);
                connection.Transport.Input.Complete();
                connection.Transport.Output.Complete();
            }
        }
    }
}
