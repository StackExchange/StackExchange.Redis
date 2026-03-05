using System.Net;
using KestrelRedisServer;
using Microsoft.AspNetCore.Connections;
using StackExchange.Redis;
using StackExchange.Redis.Server;

var server = new MemoryCacheRedisServer
{
    // note: we don't support many v6 features, but some clients
    // want this before they'll try RESP3
    RedisVersion = new(6, 0),
    // Password = "letmein",
};

/*
// demonstrate cluster spoofing
server.ServerType = ServerType.Cluster;
var ep = server.AddEmptyNode();
server.Migrate("key", ep);
*/

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<RedisServer>(server);
builder.WebHost.ConfigureKestrel(options =>
{
    // HTTP 5000 (test/debug API only)
    options.ListenLocalhost(5000);

    // this is the core of using Kestrel to create a TCP server
    // TCP 6379
    Action<Microsoft.AspNetCore.Server.Kestrel.Core.ListenOptions> builder = builder => builder.UseConnectionHandler<RedisConnectionHandler>();
    foreach (var ep in server.GetEndPoints())
    {
        if (ep is IPEndPoint ip && ip.Address.Equals(IPAddress.Loopback))
        {
            options.ListenLocalhost(ip.Port, builder);
        }
        else
        {
            options.Listen(ep, builder);
        }
    }
});

var app = builder.Build();

// redis-specific hack - there is a redis command to shutdown the server
_ = server.Shutdown.ContinueWith(
    static (t, s) =>
    {
        try
        {
            // if the resp server is shutdown by a client: stop the kestrel server too
            if (t.Result == RespServer.ShutdownReason.ClientInitiated)
            {
                ((IServiceProvider)s!).GetService<IHostApplicationLifetime>()?.StopApplication();
            }
        }
        catch { /* Don't go boom on shutdown */ }
    },
    app.Services);

// add debug route
app.Run(context => context.Response.WriteAsync(server.GetStats()));

// run the server
await app.RunAsync();
