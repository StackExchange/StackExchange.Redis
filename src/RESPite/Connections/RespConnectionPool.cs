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

    private readonly ConcurrentQueue<RespConnection> _pool = [];
    private readonly Func<RespConfiguration, RespConnection> _createConnection;
    private readonly int _count;
    private readonly RespContext _defaultTemplate;

    public ref readonly RespContext Template => ref _defaultTemplate;

    public RespConnectionPool(
        in RespContext template,
        Func<RespConfiguration, RespConnection> createConnection,
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
        Func<RespConfiguration, RespConnection> createConnection,
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
    public RespConnection GetConnection() => GetConnection(in _defaultTemplate);

    /// <summary>
    /// Borrow a connection from the pool.
    /// </summary>
    /// <param name="template">The template context to use for the leased connection; everything except the connection
    /// will be inherited by the new context.</param>
    public RespConnection GetConnection(in RespContext template)
    {
        ThrowIfDisposed();
        template.CancellationToken.ThrowIfCancellationRequested();

        if (!_pool.TryDequeue(out var connection))
        {
            connection = _createConnection(template.Connection.Configuration);
        }
        return new PoolWrapper(this, template.WithConnection(connection));
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

    private void Return(RespConnection tail)
    {
        if (_isDisposed || !tail.IsHealthy || _pool.Count >= _count)
        {
            tail.Dispose();
        }
        else
        {
            _pool.Enqueue(tail);
        }
    }

    private static RespConnection CreateConnection(RespConfiguration config, EndPoint endpoint)
    {
        Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.NoDelay = true;
        socket.Connect(endpoint);
        return new StreamConnection(config, new NetworkStream(socket));
    }

    private sealed class PoolWrapper(
        RespConnectionPool pool,
        in RespContext tail) : DecoratorConnection(tail)
    {
        protected override bool OwnsConnection => false;

        protected override void OnDispose(bool disposing)
        {
            if (disposing)
            {
                pool.Return(Tail);
            }
            base.OnDispose(disposing);
        }

        public override void Send(in RespOperation message)
        {
            ThrowIfDisposed();
            Tail.Send(message);
        }

        internal override void Send(ReadOnlySpan<RespOperation> messages)
        {
            ThrowIfDisposed();
            Tail.Send(messages);
        }

        public override Task SendAsync(in RespOperation message)
        {
            ThrowIfDisposed();
            return Tail.SendAsync(message);
        }

        internal override Task SendAsync(ReadOnlyMemory<RespOperation> messages)
        {
            ThrowIfDisposed();
            return Tail.SendAsync(messages);
        }
    }
}
