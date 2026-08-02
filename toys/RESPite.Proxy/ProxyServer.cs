using System.Buffers;
using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Text;
using RESPite.Messages;
using RESPite.Streams;

namespace RESPite.Proxy;

internal sealed class ProxyServer
{
    public ProxyServerOptions Options => _options;
    public CancellationToken Lifetime => _applicationLifetime?.ApplicationStopping ?? CancellationToken.None;

    private readonly ProxyServerOptions _options;
    private readonly WorkerPool _pool;
    private readonly IHostApplicationLifetime? _applicationLifetime;
    private readonly InnerLeg[] _inner;
    private int _roundRobin = -1;

    public ProxyServer(ProxyServerOptions options, WorkerPool pool, IHostApplicationLifetime? applicationLifetime, bool deferUpstream = false)
    {
        _options = options;
        _pool = pool;
        _applicationLifetime = applicationLifetime;

        var count = Math.Max(1, options.UpstreamConnectionCount);
        _inner = new InnerLeg[count];
        if (deferUpstream)
        {
            // The upstream legs will be INSTALLED later (InstallLeg) by a transport that has to exist
            // first -- SocketSet upstream connections need the SocketSet instance, which needs this
            // ProxyServer, so eager construction here would be circular. The caller MUST install all
            // legs before accepting clients: GetNextLeg does not tolerate holes.
            return;
        }

        for (int i = 0; i < count; i++)
        {
            var stream = Connect();
            var leg = new InnerLeg(this, stream);
            leg.StartReading(stream, sync: true, cancellationToken: Lifetime);
            _inner[i] = leg;
        }
    }

    internal int UpstreamLegCount => _inner.Length;

    internal InnerLeg CreateLeg(Stream tail) => new(this, tail);

    internal void InstallLeg(int index, InnerLeg leg) => _inner[index] = leg;

    internal InnerLeg GetLeg(int index) => _inner[index];

    // establishes an upstream connection to the backing server; the resulting stream routes its
    // socket completions through the worker pool.
    private Stream Connect()
    {
        var upstream = _options.ServerEndpoint;
        var socket = SocketUtil.CreateSocket(upstream, true);
        socket.Connect(upstream);
        return new WorkerNetworkStream(socket, _pool);
    }

    private InnerLeg GetNextLeg()
    {
        var arr = _inner;
        var index = (uint)Interlocked.Increment(ref _roundRobin) % (uint)arr.Length;
        return arr[index];
    }

    public Task RunClientAsync(IDuplexPipe transport)
    {
        // round-robin over the pool; the client stays sticky to this leg for its entire life
        // (ProxyClient captures its InnerLeg and never re-resolves it), so a single downstream
        // client never spreads commands across transports and can't be reordered. If a leg's
        // upstream connection dies we lose the ~1/N of clients pinned to it, which is acceptable.
        return GetNextLeg().RunClientAsync(transport);
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

    internal void RemoveClient(ProxyClient client)
    {
        var opCount = client.OpCount;
        if (_clients.TryRemove(client.Id, out _))
        {
            Interlocked.Add(ref _opCountFromDeadConnections, opCount);
        }
    }

    public ulong GetOpCount(out int activeClients)
    {
        ulong tally = 0;
        activeClients = 0;
        foreach (var client in _clients)
        {
            tally += client.Value.OpCount;
            activeClients++;
        }

        // intentionally delay this read
        return tally + Interlocked.Read(ref _opCountFromDeadConnections);
    }
    private ulong _opCountFromDeadConnections;

    internal sealed class InnerLeg(ProxyServer server, Stream tail) : RespStream
    {
        private readonly BufferedStreamWriter _outBuffer =
            BufferedStreamWriter.Create(true, tail, server.Options.BufferPool);

        public ProxyServer Server => server;
        public CancellationToken Lifetime => server.Lifetime;
        public MemoryPool<byte>? BufferPool => server.Options.BufferPool;

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
            // else drop on the floor - client isn't there any more!
        }

        public Task RunClientAsync(IDuplexPipe transport)
        {
            var client = new PipeProxyClient(
                this,
                transport.Output);
            server.RegisterClient(client);
            return client.ExecuteAsync(transport.Input);
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

        public void RunClient(Socket socket)
        {
            WorkerPool.DebugAssertWorker();
            var client = new SocketProxyClient(this, socket);
            server.RegisterClient(client);
            client.StartReading();
        }

#if SOCKETSET
        // Push-feed surface for a SocketSet upstream connection: replies are framed from the transport
        // loop thread via the same GetReceiveBuffer/OnAfterReceive seam the level-2 CLIENT uses, instead
        // of a parked reader thread being pulsed per completion. This removes the park/pulse hop chain on
        // the reply path, which the PING-vs-GET discriminator located as where the residual Envoy gap
        // lives (our upstream adds +96us/request against Envoy's +56us).
        public void InitTransportRead() => InitRead();

        public bool Feed(ReadOnlySpan<byte> data)
        {
            while (!data.IsEmpty)
            {
                var dest = GetReceiveBuffer();
                if (dest.IsEmpty) return false;
                int take = Math.Min(dest.Length, data.Length);
                data.Slice(0, take).CopyTo(dest.Span);
                if (!OnAfterReceive(take, inline: true)) return false;
                data = data.Slice(take);
            }
            return true;
        }

        public void CloseFromTransport(Exception? fault = null)
            => OnReceiveCleanup(fault is null ? SocketError.Success : SocketError.Fault, fault);
#endif

#if SOCKETSET
        // The third transport entry point, alongside RunClient(Socket) and RunClientAsync(IDuplexPipe).
        // Level 2: no pipes and no pump -- the caller feeds received bytes straight in from the transport
        // loop thread. Registration is identical to the SAEA path, so /stats and client-id allocation
        // behave the same across transports and the legs stay comparable.
        public SocketSetProxyClient RunClient(SocketSets.Connection conn)
        {
            var client = new SocketSetProxyClient(this, conn);
            server.RegisterClient(client);
            return client;
        }
#endif
    }

    public void RunClient(Socket socket) => GetNextLeg().RunClient(socket);

#if SOCKETSET
    /// <summary>Level-2 entry: hand the transport connection a client that frames on the loop thread.</summary>
    public SocketSetProxyClient RunClient(SocketSets.Connection conn) => GetNextLeg().RunClient(conn);

    /// <summary>Shard-AFFINE level-2 entry: the caller (on a loop thread) picked the leg living on its
    /// own shard, so forward and reply for this client never cross threads.</summary>
    public SocketSetProxyClient RunClient(SocketSets.Connection conn, int legIndex) => GetLeg(legIndex).RunClient(conn);
#endif
}

[AsciiHash("+OK\r\n")]
internal static partial class RespOK { }
