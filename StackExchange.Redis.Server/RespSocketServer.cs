using System;
using System.Net;
using System.Threading.Tasks;
using Pipelines.Sockets.Unofficial;

namespace StackExchange.Redis.Server
{
    public sealed class RespSocketServer : SocketServer
    {
        private readonly RespServer _server;
        public RespSocketServer(RespServer server)
        {
            _server = server ?? throw new ArgumentNullException(nameof(server));
            server.Shutdown.ContinueWith((_, o) => ((SocketServer)o).Dispose(), this);
        }
        protected override void OnStarted(EndPoint endPoint)
            => _server.Log("Server is listening on " + endPoint);

        protected override Task OnClientConnectedAsync(in ClientConnection client)
            => _server.RunClientAsync(client.Transport);

        protected override void Dispose(bool disposing)
        {
            if (disposing) _server.Dispose();
        }
    }
}
