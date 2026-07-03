using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using RESPite.Streams;

namespace RESPite.Proxy;

internal sealed class ProxySocketServer(ProxyServer server, WorkerPool pool) : SocketServer(pool)
{
    internal override void InitClient(Socket socket) => server.RunClient(socket);
}
internal abstract class SocketServer : IDisposable
{
    private bool _isDisposed;
    private readonly ParameterizedThreadStart _accept;

    private readonly List<Socket> _sockets = [];
    private readonly WorkerPool _pool;
    public SocketServer(WorkerPool workerPool)
    {
        _pool = workerPool;
        _accept = state => Accept((Socket)state!);
    }

    public void Dispose()
    {
        _isDisposed = true;
        lock (_sockets)
        {
            foreach (var socket in _sockets)
            {
                socket.Dispose();
            }
            _sockets.Clear();
        }
    }

    private void Accept(Socket socket)
    {
        try
        {
            lock (_sockets)
            {
                if (_isDisposed) return;
                _sockets.Add(socket);
            }

            using (socket)
            {
                while (!Volatile.Read(ref _isDisposed))
                {
                    _pool.Enqueue(this, WorkerStep.InitClient, socket.Accept());
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.Message);
        }
        finally
        {
            lock (_sockets)
            {
                _sockets.Remove(socket);
            }
            socket.Dispose();
        }
    }

    internal abstract void InitClient(Socket socket);

    public void Start(EndPoint endpoint)
    {
        var socket = SocketUtil.CreateSocket(endpoint, true);
        try
        {
            socket.Bind(endpoint);
            socket.Listen(1024);
            var thread = new Thread(_accept);
            thread.Priority = ThreadPriority.AboveNormal;
            thread.IsBackground = false;
            thread.Name = "socket accept";
            thread.Start(socket);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
