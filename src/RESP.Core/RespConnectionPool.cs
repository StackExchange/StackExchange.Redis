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

    public RespConnectionPool(Func<RespConfiguration, IRespConnection> createConnection, RespConfiguration? configuration = null, int count = RespConnectionPool.DefaultCount)
    {
        _createConnection = createConnection;
        _count = count;
        _configuration = configuration ?? RespConfiguration.Default;
    }

    public RespConnectionPool(IPAddress? address = null, int port = 6379, RespConfiguration? configuration = null, int count = DefaultCount)
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

    public IRespConnection GetConnection()
    {
        ThrowIfDisposed();
        if (!_pool.TryDequeue(out var connection))
        {
            connection = _createConnection(_configuration);
        }
        return new PoolWrapper(this, connection);
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

    private sealed class PoolWrapper(RespConnectionPool pool, IRespConnection tail) : IRespConnection
    {
        private bool _isDisposed;
        public void Dispose()
        {
            _isDisposed = true;
            pool.Return(tail);
        }

        public bool CanWrite => !_isDisposed && tail.CanWrite;

        public int Outstanding => tail.Outstanding;

        public RespConfiguration Configuration => tail.Configuration;
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
            tail.Send(message);
        }

        public Task SendAsync(IRespMessage message, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return tail.SendAsync(message, cancellationToken);
        }
    }
}
