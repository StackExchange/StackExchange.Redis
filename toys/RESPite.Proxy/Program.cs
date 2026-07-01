using System.Buffers;
using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Connections;
using RESPite.Proxy;
using RESPite.Streams;

var proxyOptions = new ProxyServerOptions
{
    Password = "letmein",
};

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(proxyOptions);
builder.Services.AddSingleton<ProxyServer>();
builder.WebHost.ConfigureKestrel(options =>
{
    // HTTP 5000 (test/debug API only)
    options.ListenLocalhost(5000);

    // this is the core of using Kestrel to create a TCP server
    // TCP 6379
    Action<Microsoft.AspNetCore.Server.Kestrel.Core.ListenOptions> listenBuilder
        = static options => options.UseConnectionHandler<ProxyHandler>();

    foreach (var ep in proxyOptions.GetListenEndpoints())
    {
        if (ep is IPEndPoint ip && ip.Address.Equals(IPAddress.Loopback))
        {
            options.ListenLocalhost(ip.Port, listenBuilder);
        }
        else
        {
            options.Listen(ep, listenBuilder);
        }
    }
});

var app = builder.Build();

// run the server
await app.RunAsync();

public class ProxyServerOptions
{
    public string Password { get; set; } = "";

    public IEnumerable<EndPoint> GetListenEndpoints()
    {
        yield return new IPEndPoint(IPAddress.Loopback, 6380);
    }

    public EndPoint ServerEndpoint { get; set; } = new IPEndPoint(IPAddress.Loopback, 6379);
    public MemoryPool<byte>? BufferPool { get; set; }

    public Stream Connect()
    {
        var upstream = ServerEndpoint;
        var socket = SocketUtil.CreateSocket(upstream, true);
        socket.Connect(upstream);
        return new NetworkStream(socket);
    }
}
