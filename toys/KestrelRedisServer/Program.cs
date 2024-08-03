using KestrelRedisServer;
using Microsoft.AspNetCore.Connections;
using StackExchange.Redis.Server;

var server = new MemoryCacheRedisServer();

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<RespServer>(server);
builder.WebHost.ConfigureKestrel(options =>
{
    // HTTP 5000 (test/debug API only)
    options.ListenLocalhost(5000);

    // this is the core of using Kestrel to create a TCP server
    // TCP 6379
    options.ListenLocalhost(6379, builder => builder.UseConnectionHandler<RedisConnectionHandler>());
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
