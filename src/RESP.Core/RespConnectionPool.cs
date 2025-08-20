using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Resp;

public sealed class RespConnectionPool(Func<IRespConnection> createConnection, int count = RespConnectionPool.DefaultCount) : IDisposable
{
    private const int DefaultCount = 10;
    private bool _isDisposed;

    public RespConnectionPool(EndPoint endPoint, int count = DefaultCount) : this(() => CreateConnection(endPoint), count)
    {
    }

    private readonly ConcurrentQueue<IRespConnection> _pool = [];

    public IRespConnection GetConnection()
    {
        ThrowIfDisposed();
        if (!_pool.TryDequeue(out var connection))
        {
            connection = createConnection();
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

    private static IRespConnection CreateConnection(EndPoint endpoint)
    {
        Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.NoDelay = true;
        socket.Connect(endpoint);
        return new DirectWriteConnection(new NetworkStream(socket));
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

        public RespPayload Send(RespPayload payload)
        {
            ThrowIfDisposed();
            return tail.Send(payload);
        }

        public ValueTask<RespPayload> SendAsync(RespPayload payload, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return tail.SendAsync(payload, cancellationToken);
        }
    }
}
