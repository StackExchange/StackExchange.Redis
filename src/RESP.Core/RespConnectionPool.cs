using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Resp;

public sealed class RespConnectionPool(Func<RespConfiguration, IRespConnection> createConnection, RespConfiguration? configuration = null, int count = RespConnectionPool.DefaultCount) : IDisposable
{
    private readonly RespConfiguration _configuration = configuration ?? RespConfiguration.Default;
    private const int DefaultCount = 10;
    private bool _isDisposed;

    public RespConnectionPool(IPAddress? address = null, int port = 6379, RespConfiguration? configuration = null, int count = DefaultCount)
        : this(new IPEndPoint(address ?? IPAddress.Loopback, port), configuration, count)
    {
    }
    public RespConnectionPool(EndPoint endPoint, RespConfiguration? configuration = null, int count = DefaultCount)
        : this(config => CreateConnection(config, endPoint), configuration, count)
    {
    }

    private readonly ConcurrentQueue<IRespConnection> _pool = [];

    public IRespConnection GetConnection()
    {
        ThrowIfDisposed();
        if (!_pool.TryDequeue(out var connection))
        {
            connection = createConnection(_configuration);
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
        if (!tail.CanWrite || _pool.Count >= count)
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
        return new DirectWriteConnection(config, new NetworkStream(socket));
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
