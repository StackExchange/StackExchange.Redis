using System.Collections.Concurrent;
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using RESPite.Connections.Internal;
using RESPite.Internal;

namespace RESPite.Connections;

public sealed class RespConnectionPool : IDisposable
{
    private const int DefaultCount = 10;
    private bool _isDisposed;

    [Obsolete("This is for testing only")]
    [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
    public bool UseCustomNetworkStream { get; set; }

    private readonly ConcurrentQueue<IRespConnection> _pool = [];
    private readonly Func<RespConfiguration, IRespConnection> _createConnection;
    private readonly int _count;
    private readonly RespContext _defaultTemplate;

    public ref readonly RespContext Template => ref _defaultTemplate;

    public RespConnectionPool(
        in RespContext template,
        Func<RespConfiguration, IRespConnection> createConnection,
        int count = DefaultCount)
    {
        _createConnection = createConnection;
        _count = count;
        template.CancellationToken.ThrowIfCancellationRequested();
        // swap out the connection for a dummy (retaining the configuration)
        var configuredConnection = NullConnection.WithConfiguration(template.Connection.Configuration);
        _defaultTemplate = template.WithConnection(configuredConnection);
    }

    public RespConnectionPool(
        Func<RespConfiguration, IRespConnection> createConnection,
        int count = DefaultCount) : this(RespContext.Null, createConnection, count)
    {
    }

    public RespConnectionPool(
        in RespContext template,
        IPAddress? address = null,
        int port = 6379,
        int count = DefaultCount)
        : this(in template, new IPEndPoint(address ?? IPAddress.Loopback, port), count)
    {
    }

    public RespConnectionPool(
        IPAddress? address = null,
        int port = 6379,
        int count = DefaultCount) : this(RespContext.Null, address, port, count)
    {
    }

    public RespConnectionPool(EndPoint endPoint, int count = DefaultCount)
        : this(RespContext.Null, endPoint, count)
    {
    }

    public RespConnectionPool(in RespContext template, EndPoint endPoint, int count = DefaultCount)
        : this(template, config => CreateConnection(config, endPoint), count)
    {
    }

    /// <summary>
    /// Borrow a connection from the pool, using the default template.
    /// </summary>
    public IRespConnection GetConnection() => GetConnection(in _defaultTemplate);

    /// <summary>
    /// Borrow a connection from the pool.
    /// </summary>
    /// <param name="template">The template context to use for the leased connection; everything except the connection
    /// will be inherited by the new context.</param>
    public IRespConnection GetConnection(in RespContext template)
    {
        ThrowIfDisposed();
        template.CancellationToken.ThrowIfCancellationRequested();

        if (!_pool.TryDequeue(out var connection))
        {
            connection = _createConnection(template.Connection.Configuration);
        }

        return new PoolWrapper(this, connection, in template);
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

    private static IRespConnection CreateConnection(RespConfiguration config, EndPoint endpoint)
    {
        Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.NoDelay = true;
        socket.Connect(endpoint);
        return new StreamConnection(config, new NetworkStream(socket));
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
            in RespContext template)
        {
            _pool = pool;
            _tail = tail;
            _context = template.WithConnection(this);
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

        public void Send(in RespOperation message)
        {
            ThrowIfDisposed();
            _tail.Send(message);
        }

        public void Send(ReadOnlySpan<RespOperation> messages)
        {
            ThrowIfDisposed();
            _tail.Send(messages);
        }

        public Task SendAsync(in RespOperation message)
        {
            ThrowIfDisposed();
            return _tail.SendAsync(message);
        }

        public Task SendAsync(ReadOnlyMemory<RespOperation> messages)
        {
            ThrowIfDisposed();
            return _tail.SendAsync(messages);
        }
    }
}
