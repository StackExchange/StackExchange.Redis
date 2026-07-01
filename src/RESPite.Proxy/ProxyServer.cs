using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using RESPite.Streams;

namespace RESPite.Proxy;

internal sealed class ProxyServer
{
    public ProxyServerOptions Options => _options;
    public CancellationToken Lifetime => _applicationLifetime.ApplicationStopping;

    private ConcurrentDictionary<int, ProxyClient> _clients = new();
    private int _nextClientId = 0;
    private readonly ProxyServerOptions _options;
    private readonly IHostApplicationLifetime _applicationLifetime;

    public ProxyServer(ProxyServerOptions options, IHostApplicationLifetime applicationLifetime)
    {
        _options = options;
        _applicationLifetime = applicationLifetime;
        var stream = options.Connect();
        var inner = new InnerLeg(this, stream);
        inner.StartReading(sync: true, cancellationToken: Lifetime);
    }

    private readonly Queue<int> _inFlightOwners = new();

    public Task RunClientAsync(IDuplexPipe transport)
    {
        ProxyClient client = new(this, Interlocked.Increment(ref _nextClientId), transport);
        if (!_clients.TryAdd(client.Id, client)) return Task.CompletedTask;
        return client.ExecuteAsync();
    }

    private sealed class InnerLeg(ProxyServer server, Stream tail) : RespStream(tail)
    {
        protected override void OnReadFrame(ReadOnlySpan<byte> frame, ref IMemoryOwner<byte>? memoryOwner)
            => server.OnResponse(frame);
    }

    private void OnResponse(ReadOnlySpan<byte> frame)
    {
        int clientId;
        lock (_inFlightOwners)
        {
            if (!_inFlightOwners.TryDequeue(out clientId)) return;
        }
        if (_clients.TryGetValue(clientId, out var client))
        {
            client?.SendResponse(frame);
        }
    }
}
