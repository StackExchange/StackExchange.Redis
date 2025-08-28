using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Resp;

public sealed class RespConnectionPool : IDisposable
{
    private readonly RespConfiguration _configuration;
    private const int DefaultCount = 10;
    private bool _isDisposed;

    [Obsolete("This is for testing only")]
    [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
    public bool UseCustomNetworkStream { get; set; }

    private readonly ConcurrentQueue<IRespConnection> _pool = [];
    private readonly Func<RespConfiguration, IRespConnection> _createConnection;
    private readonly int _count;

    public RespConnectionPool(
        Func<RespConfiguration, IRespConnection> createConnection,
        RespConfiguration? configuration = null,
        int count = RespConnectionPool.DefaultCount)
    {
        _createConnection = createConnection;
        _count = count;
        _configuration = configuration ?? RespConfiguration.Default;
    }

    public RespConnectionPool(
        IPAddress? address = null,
        int port = 6379,
        RespConfiguration? configuration = null,
        int count = DefaultCount)
        : this(new IPEndPoint(address ?? IPAddress.Loopback, port), configuration, count)
    {
    }

    public RespConnectionPool(EndPoint endPoint, RespConfiguration? configuration = null, int count = DefaultCount)
    {
#pragma warning disable CS0618 // Type or member is obsolete
        _createConnection = config => CreateConnection(config, endPoint, UseCustomNetworkStream);
#pragma warning restore CS0618 // Type or member is obsolete
        _count = count;
        _configuration = configuration ?? RespConfiguration.Default;
    }

    /// <summary>
    /// Borrow a connection from the pool.
    /// </summary>
    /// <param name="database">The database to override in the context of the leased connection.</param>
    /// <param name="cancellationToken">The cancellation token to override in the context of the leased connection.</param>
    public IRespConnection GetConnection(int? database = null, CancellationToken? cancellationToken = null)
    {
        ThrowIfDisposed();
        if (cancellationToken.HasValue)
        {
            cancellationToken.GetValueOrDefault().ThrowIfCancellationRequested();
        }

        if (!_pool.TryDequeue(out var connection))
        {
            connection = _createConnection(_configuration);
        }

        return new PoolWrapper(this, connection, database, cancellationToken);
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed) Throw();
        static void Throw() => throw new ObjectDisposedException(nameof(RespConnectionPool));
    }

    public void Dispose()
    {
        _isDisposed = true;
        while (_pool.TryDequeue(out var connection))
        {
            connection.Dispose();
        }
    }

    private void Return(IRespConnection tail)
    {
        if (_isDisposed || !tail.CanWrite || _pool.Count >= _count)
        {
            tail.Dispose();
        }
        else
        {
            _pool.Enqueue(tail);
        }
    }

    private static IRespConnection CreateConnection(RespConfiguration config, EndPoint endpoint, bool useCustom)
    {
        Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.NoDelay = true;
        socket.Connect(endpoint);
        return new DirectWriteConnection(config, Wrap(socket, useCustom));

        static Stream Wrap(Socket socket, bool useCustom)
        {
#if NETCOREAPP3_0_OR_GREATER
            if (useCustom) return new CustomNetworkStream(socket);
#endif
            return new NetworkStream(socket);
        }
    }

    private sealed class PoolWrapper : IRespConnection
    {
        private bool _isDisposed;
        private readonly RespConnectionPool _pool;
        private readonly IRespConnection _tail;
        private readonly RespContext _context;

        public ref readonly RespContext Context => ref _context;

        public PoolWrapper(
            RespConnectionPool pool,
            IRespConnection tail,
            int? database,
            CancellationToken? cancellationToken)
        {
            _pool = pool;
            _tail = tail;
            _context = RespContext.For(this);
            if (database.HasValue) _context = _context.WithDatabase(database.GetValueOrDefault());
            if (cancellationToken.HasValue)
                _context = _context.WithCancellationToken(cancellationToken.GetValueOrDefault());
        }

        public void Dispose()
        {
            _isDisposed = true;
            _pool.Return(_tail);
        }

        public bool CanWrite => !_isDisposed && _tail.CanWrite;

        public int Outstanding => _tail.Outstanding;

        public RespConfiguration Configuration => _tail.Configuration;

        private void ThrowIfDisposed()
        {
            if (_isDisposed) Throw();
            static void Throw() => throw new ObjectDisposedException(nameof(PoolWrapper));
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return default;
        }

        public void Send(IRespMessage message)
        {
            ThrowIfDisposed();
            _tail.Send(message);
        }

        public void Send(ReadOnlySpan<IRespMessage> messages)
        {
            ThrowIfDisposed();
            _tail.Send(messages);
        }

        public Task SendAsync(IRespMessage message)
        {
            ThrowIfDisposed();
            return _tail.SendAsync(message);
        }

        public Task SendAsync(ReadOnlyMemory<IRespMessage> messages)
        {
            ThrowIfDisposed();
            return _tail.SendAsync(messages);
        }
    }
}
