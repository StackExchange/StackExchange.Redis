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
    private readonly InnerLeg _inner;

    public ProxyServer(ProxyServerOptions options, IHostApplicationLifetime applicationLifetime)
    {
        _options = options;
        _applicationLifetime = applicationLifetime;
        var stream = options.Connect();
        _inner = new InnerLeg(this, stream);
        _inner.StartReading(sync: true, cancellationToken: Lifetime);
    }

    public Task RunClientAsync(IDuplexPipe transport) => _inner.RunClientAsync(transport);

    internal sealed class InnerLeg(ProxyServer server, Stream tail) : RespStream(tail)
    {
        private readonly BufferedStreamWriter _outBuffer =
            BufferedStreamWriter.Create(true, tail, server.Options.BufferPool);

        public ProxyServer Server => server;
        public CancellationToken Lifetime => server.Lifetime;

        private readonly Queue<int> _inFlightOwners = new();
        private readonly ConcurrentDictionary<int, ProxyClient> _clients = new();
        private int _nextClientId = 0;

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
            else if (_clients.TryGetValue(clientId, out var client))
            {
                client.SendRaw(frame);
            }
            else
            {
                Console.Error.WriteLine("Client unavailable");
            }
        }

        private const int SelfId = 0;
        public Task RunClientAsync(IDuplexPipe transport)
        {
            ProxyClient client = new(
                this,
                transport.Input.AsStream(),
                transport.Output);
            do
            {
                var id = Interlocked.Increment(ref _nextClientId);
                if (id is SelfId) continue;
                client.Id = id;
            }
            // loop until we succeed
            while (!_clients.TryAdd(client.Id, client));

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

        public void Remove(ProxyClient client) => _clients.TryRemove(client.Id, out _);
    }
}

[AsciiHash("+OK\r\n")]
internal static partial class RespOK { }
