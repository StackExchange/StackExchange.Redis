using System.Buffers;
using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Text;
using RESPite.Messages;
using RESPite.Streams;

namespace RESPite.Proxy;

internal sealed class ProxyServer
{
    public ProxyServerOptions Options => _options;
    public CancellationToken Lifetime => _applicationLifetime.ApplicationStopping;

    private readonly ProxyServerOptions _options;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly InnerLeg[] _inner;
    private int _roundRobin = -1;

    public ProxyServer(ProxyServerOptions options, IHostApplicationLifetime applicationLifetime)
    {
        _options = options;
        _applicationLifetime = applicationLifetime;

        var count = Math.Max(1, options.UpstreamConnectionCount);
        _inner = new InnerLeg[count];
        for (int i = 0; i < count; i++)
        {
            var stream = options.Connect();
            var leg = new InnerLeg(this, stream);
            leg.StartReading(sync: true, cancellationToken: Lifetime);
            _inner[i] = leg;
        }
    }

    public Task RunClientAsync(IDuplexPipe transport)
    {
        // round-robin over the pool; the client stays sticky to this leg for its entire life
        // (ProxyClient captures its InnerLeg and never re-resolves it), so a single downstream
        // client never spreads commands across transports and can't be reordered. If a leg's
        // upstream connection dies we lose the ~1/N of clients pinned to it, which is acceptable.
        var index = (uint)Interlocked.Increment(ref _roundRobin) % (uint)_inner.Length;
        return _inner[index].RunClientAsync(transport);
    }

    // Client identity is server-global so CLIENT ID is unique across the whole pool, not per-leg.
    internal const int SelfId = 0;
    private readonly ConcurrentDictionary<int, ProxyClient> _clients = new();
    private int _nextClientId = SelfId;

    internal void RegisterClient(ProxyClient client)
    {
        do
        {
            var id = Interlocked.Increment(ref _nextClientId);
            if (id is SelfId) continue; // reserved sentinel; skip on wrap-around
            client.Id = id;
        }
        // loop until we succeed
        while (!_clients.TryAdd(client.Id, client));
    }

    internal bool TryGetClient(int id, out ProxyClient client) => _clients.TryGetValue(id, out client!);

    internal void RemoveClient(ProxyClient client) => _clients.TryRemove(client.Id, out _);

    internal sealed class InnerLeg(ProxyServer server, Stream tail) : RespStream(tail)
    {
        private readonly BufferedStreamWriter _outBuffer =
            BufferedStreamWriter.Create(true, tail, server.Options.BufferPool);

        public ProxyServer Server => server;
        public CancellationToken Lifetime => server.Lifetime;

        private readonly Queue<int> _inFlightOwners = new();

        protected override void OnReadFrame(RespPrefix prefix, ReadOnlySpan<byte> frame, ref IMemoryOwner<byte>? memoryOwner)
        {
            int clientId;
            lock (_inFlightOwners)
            {
                if (!_inFlightOwners.TryDequeue(out clientId))
                {
                    Console.Error.WriteLine("No pending message!");
                    return;
                }
            }

            if (clientId is SelfId)
            {
                // SELECT etc
                if (!RespOK.IsCI(frame, AsciiHash.HashUC(frame))) Throw();
                static void Throw() => throw new InvalidOperationException("Invalid response from server - SELECT?");
            }
            else if (server.TryGetClient(clientId, out var client))
            {
                client.ForwardResponse(frame);
            }
            else
            {
                // drop on the floor - client isn't there any more!
            }
        }

        public Task RunClientAsync(IDuplexPipe transport)
        {
            ProxyClient client = new(
                this,
                transport.Input.AsStream(),
                transport.Output);
            server.RegisterClient(client);
            return client.ExecuteAsync();
        }

        private int _db;
        public void Send(int db, ProxyClient client, ReadOnlySpan<byte> frame)
        {
            lock (_inFlightOwners)
            {
                if (db != _db) WriteSelectInsideLock(db);

                _inFlightOwners.Enqueue(client.Id);
                _outBuffer.Write(frame);
                _outBuffer.Flush();
            }
        }

        private void WriteSelectInsideLock(int db)
        {
            Debug.Assert(Monitor.IsEntered(_inFlightOwners), "should hold lock");
            _inFlightOwners.Enqueue(SelfId);

            Span<byte> intBuffer = stackalloc byte[9]; // keep < 10 bytes, so length is single-char
            if (!Utf8Formatter.TryFormat(db, intBuffer, out var bytes)) Throw();

            Span<byte> buffer = stackalloc byte[32];
            ReadOnlySpan<byte> select = "*2\r\n$6\r\nSELECT\r\n$X\r\n"u8;
            select.CopyTo(buffer);
            Debug.Assert(buffer[17] == (byte)'X', "expecting to replace length placeholder");
            buffer[17] = (byte)('0' + bytes);
            intBuffer.Slice(0, bytes).CopyTo(buffer.Slice(select.Length));
            "\r\n"u8.CopyTo(buffer.Slice(select.Length + bytes));
            _outBuffer.Write(buffer.Slice(0, select.Length + bytes + 2));
            _db = db;

            static void Throw() => throw new FormatException("Unable to format SELECT");
        }

        public void Remove(ProxyClient client) => server.RemoveClient(client);
    }
}

[AsciiHash("+OK\r\n")]
internal static partial class RespOK { }
