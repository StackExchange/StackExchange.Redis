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
    private readonly Func<RespConfiguration, CancellationToken, ValueTask<RespConnection>> _createConnection;
    private readonly int _count;
    private readonly RespContext _defaultTemplate;

    public ref readonly RespContext Template => ref _defaultTemplate;

    public event EventHandler<RespConnection.RespConnectionErrorEventArgs>? ConnectionError;

    private void OnConnectionError(object? sender, RespConnection.RespConnectionErrorEventArgs e)
        => ConnectionError?.Invoke(this, e); // mask sender

    private readonly EventHandler<RespConnection.RespConnectionErrorEventArgs> _onConnectionError;

    public RespConnectionPool() : this(RespContext.Null, "127.0.0.1", 6379) { }

    public RespConnectionPool(
        in RespContext template,
        Func<RespConfiguration, CancellationToken, ValueTask<RespConnection>> createConnection,
        int count = DefaultCount)
    {
        _createConnection = createConnection;
        _count = count;
        template.CancellationToken.ThrowIfCancellationRequested();
        // swap out the connection for a dummy (retaining the configuration)
        var configuredConnection = NullConnection.WithConfiguration(template.Connection.Configuration);
        _defaultTemplate = template.WithConnection(configuredConnection);
        _onConnectionError = OnConnectionError;
    }

    public RespConnectionPool(
        in RespContext template,
        string endpoint,
        int port,
        int count = DefaultCount,
        RespConnectionFactory? connectionFactory = null)
        : this(template, MakeCreateConnection(endpoint, port, connectionFactory), count)
    {
    }

    private static Func<RespConfiguration, CancellationToken, ValueTask<RespConnection>> MakeCreateConnection(
        string endpoint,
        int port,
        RespConnectionFactory? connectionFactory)
    {
        connectionFactory ??= RespConnectionFactory.Default;
        return (config, cancellationToken)
            => connectionFactory.ConnectAsync(endpoint, port, config, cancellationToken);
    }

    /// <summary>
    /// Borrow a connection from the pool, using the default template.
    /// </summary>
    public RespConnection GetConnection(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.CanBeCanceled)
        {
            var context = _defaultTemplate.WithCancellationToken(cancellationToken);
            return GetConnection(in context);
        }
        else
        {
            return GetConnection(in _defaultTemplate);
        }
    }

    public RespConnection GetConnection(in RespContext template) // sync over async
    {
        var pending = GetConnectionAsync(in template);
        if (!pending.IsCompleted) return pending.AsTask().GetAwaiter().GetResult();
        return pending.GetAwaiter().GetResult();
    }

    /// <summary>
    /// Borrow a connection from the pool, using the default template.
    /// </summary>
    public ValueTask<RespConnection> GetConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.CanBeCanceled)
        {
            var context = _defaultTemplate.WithCancellationToken(cancellationToken);
            return GetConnectionAsync(in context);
        }
        else
        {
            return GetConnectionAsync(in _defaultTemplate);
        }
    }

    /// <summary>
    /// Borrow a connection from the pool.
    /// </summary>
    /// <param name="template">The template context to use for the leased connection; everything except the connection
    /// will be inherited by the new context.</param>
    public ValueTask<RespConnection> GetConnectionAsync(in RespContext template)
    {
        ThrowIfDisposed();
        template.CancellationToken.ThrowIfCancellationRequested();

        if (_pool.TryDequeue(out var connection)) return new(connection);

        var pending = _createConnection(template.Connection.Configuration, template.CancellationToken);
        if (!pending.IsCompleted) return Awaited(template, pending);

        connection = pending.GetAwaiter().GetResult();
        connection.ConnectionError += _onConnectionError;
        connection = new PoolWrapper(this, template.WithConnection(connection));
        return new(connection);
    }

    private async ValueTask<RespConnection> Awaited(RespContext template, ValueTask<RespConnection> pending)
    {
        var connection = await pending.ConfigureAwait(false);
        connection.ConnectionError += _onConnectionError;
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

    private sealed class PoolWrapper(
        RespConnectionPool pool,
        in RespContext tail) : DecoratorConnection(tail)
    {
        protected override bool OwnsConnection => false;

        private const string ConnectionErrorNotSupportedMessage =
            $"{nameof(ConnectionError)} events are not supported on pooled connections; use {nameof(RespConnectionPool)}.{nameof(RespConnectionPool.ConnectionError)} instead";

        public override event EventHandler<RespConnectionErrorEventArgs>? ConnectionError
        {
            add => throw new NotSupportedException(ConnectionErrorNotSupportedMessage);
            remove => throw new NotSupportedException(ConnectionErrorNotSupportedMessage);
        }

        protected override void OnDispose(bool disposing)
        {
            if (disposing)
            {
                pool.Return(Tail);
            }

            base.OnDispose(disposing);
        }

        public override void Write(in RespOperation message)
        {
            ThrowIfDisposed();
            Tail.Write(message);
        }

        internal override void Write(ReadOnlySpan<RespOperation> messages)
        {
            ThrowIfDisposed();
            Tail.Write(messages);
        }

        public override Task WriteAsync(in RespOperation message)
        {
            ThrowIfDisposed();
            return Tail.WriteAsync(message);
        }

        internal override Task WriteAsync(ReadOnlyMemory<RespOperation> messages)
        {
            ThrowIfDisposed();
            return Tail.WriteAsync(messages);
        }
    }
}
