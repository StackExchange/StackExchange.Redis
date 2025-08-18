using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Resp;

internal sealed class ConnectionPool(Func<IRespConnection> createConnection, int count = 10) : IDisposable
{
    private bool _isDisposed;

    public ConnectionPool(EndPoint endPoint, int count = 10) : this(() => CreateConnection(endPoint), count)
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
        static void Throw() => throw new ObjectDisposedException(nameof(ConnectionPool));
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
        if (_pool.Count >= count)
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
        return new RespConnection(new NetworkStream(socket));
    }

    private sealed class PoolWrapper(ConnectionPool pool, IRespConnection tail) : IRespConnection
    {
        private bool _isDisposed;
        public void Dispose()
        {
            _isDisposed = true;
            pool.Return(tail);
        }

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
