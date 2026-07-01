using Microsoft.AspNetCore.Connections;

namespace RESPite.Proxy;

internal sealed class ProxyHandler(ProxyServer server) : ConnectionHandler
{
    public override Task OnConnectedAsync(ConnectionContext connection)
        => server.RunClientAsync(connection.Transport);
}
