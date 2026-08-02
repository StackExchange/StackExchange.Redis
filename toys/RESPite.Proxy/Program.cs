using System.Buffers;
using System.Net;
using System.Net.Sockets;
using RESPite.Proxy;
using RESPite.Streams;

// --- transport selection, so the SAME proxy core can be measured on either socket layer -------------
// usage: resp-proxy [--transport worker|socketset] [--backend io-uring|epoll|managed] [--shards N]
//                   [--port N] [--upstream-port N] [--upstream-connections N] [--l2]
// --l2 selects the LEVEL-2 client: RESP framing on the transport loop thread, no pipes/pump/hop.
string transport = "worker", backend = "io-uring";
int shards = 12, listenPort = 6380, upstreamPort = 6379, upstreamConns = 5;
bool level2 = false;   // --l2: frame on the loop thread (no pipes, no pump, no hop)
bool ssUpstream = false; // --ss-upstream: upstream legs as SocketSet outbound connections (no parked reader)
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--transport" when i + 1 < args.Length: transport = args[++i]; break;
        case "--backend" when i + 1 < args.Length: backend = args[++i]; break;
        case "--l2": level2 = true; break;
        case "--ss-upstream": ssUpstream = true; break;
        case "--shards" when i + 1 < args.Length && int.TryParse(args[i + 1], out var s): shards = s; i++; break;
        case "--port" when i + 1 < args.Length && int.TryParse(args[i + 1], out var p): listenPort = p; i++; break;
        case "--upstream-port" when i + 1 < args.Length && int.TryParse(args[i + 1], out var up): upstreamPort = up; i++; break;
        case "--upstream-connections" when i + 1 < args.Length && int.TryParse(args[i + 1], out var uc): upstreamConns = uc; i++; break;
        default: Console.Error.WriteLine($"unknown argument: {args[i]}"); return 1;
    }
}

var proxyOptions = new ProxyServerOptions
{
    Password = "letmein",
    UpstreamConnectionCount = upstreamConns,
    ServerEndpoint = new IPEndPoint(IPAddress.Loopback, upstreamPort),
};
using var pool = new WorkerPool(workers: 0); // use CPU count
// --ss-upstream defers leg construction: the legs need the SocketSet instance, which needs the proxy.
var proxy = new ProxyServer(proxyOptions, pool, applicationLifetime: null, deferUpstream: ssUpstream);

// TRUST THE BANNER, NOT THE FLAG. A rig that gates on the flag it passed cannot tell a transport that
// took from one that silently fell back — and a fallback measures as a perfectly plausible result. This
// line is what a benchmark harness should read before scoring anything.
IDisposable listener;
switch (transport)
{
#if SOCKETSET
    case "socketset":
        var ssFactory = backend switch
        {
            "io-uring" => SocketSets.SocketSetFactory.IoUring,
            "epoll" => SocketSets.SocketSetFactory.Epoll,
            "managed" => SocketSets.SocketSetFactory.Managed,
            _ => throw new ArgumentException($"unknown backend '{backend}'"),
        };
        var ssOptions = new SocketSets.SocketSetOptions
        {
            Factory = ssFactory,
            Shards = shards,
        };
        var ss = new SocketSetProxyServer(proxy, ssOptions, level2);
        // Legs BEFORE Listen: GetNextLeg does not tolerate holes, and a client could arrive immediately.
        if (ssUpstream) ss.ConnectUpstream(new IPEndPoint(IPAddress.Loopback, upstreamPort), TimeSpan.FromSeconds(10));
        ss.Listen(new IPEndPoint(IPAddress.Loopback, listenPort));
        listener = ss;
        // The bridge mode is part of the banner because it is the whole experiment: a rig must be able to
        // tell a level-2 run from a level-1 one without trusting the flag it passed.
        Console.WriteLine($"[resp-proxy] transport=socketset/{backend} shards={shards} " +
                          $"bridge={(level2 ? "direct" : "pipe")} " +
                          $"upstream={(ssUpstream ? "socketset" : "worker-stream")} " +
                          $"port={listenPort} upstream-port={upstreamPort} legs={upstreamConns}");
        break;
#endif
    case "worker":
        pool.AddDebugLog(Console.WriteLine);
        var ws = new ProxySocketServer(proxy, pool);
        ws.Start(new IPEndPoint(IPAddress.Loopback, listenPort));
        listener = ws;
        Console.WriteLine($"[resp-proxy] transport=worker-saea port={listenPort} " +
                          $"upstream={upstreamPort} legs={upstreamConns}");
        break;
    default:
        Console.Error.WriteLine($"unknown/unavailable transport '{transport}' " +
                                "(was the SocketSet sibling checkout present at build time?)");
        return 1;
}
using var _listener = listener;

ulong lastOpCount = 0;
int lastActiveClients = 0;
bool first = true;
while (true)
{
    var opCount = proxy.GetOpCount(out var activeClients);
    if (first || opCount != lastOpCount || activeClients != lastActiveClients)
    {
        Console.WriteLine($"Active clients: {activeClients}; commands processed: {opCount}");
        lastOpCount = opCount;
        lastActiveClients = activeClients;
        first = false;
    }
    await Task.Delay(TimeSpan.FromSeconds(5));
}
/*

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
*/

public class ProxyServerOptions
{
    public string Password { get; set; } = "";

    public IEnumerable<EndPoint> GetListenEndpoints()
    {
        yield return new IPEndPoint(IPAddress.Loopback, 6380);
    }

    public EndPoint ServerEndpoint { get; set; } = new IPEndPoint(IPAddress.Loopback, 6379);
    public MemoryPool<byte>? BufferPool { get; set; }

    /// <summary>
    /// Number of upstream connections (InnerLeg instances) to establish; incoming clients are
    /// distributed (round-robin) over the pool and remain sticky to their assigned leg for life.
    /// </summary>
    public int UpstreamConnectionCount { get; set; } = 5;
}
