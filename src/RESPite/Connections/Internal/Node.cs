using RESPite.Internal;

namespace RESPite.Connections.Internal;

internal sealed class Node : IDisposable, IAsyncDisposable, IRespContextSource
{
    private bool _isDisposed;
    public override string ToString() => Label;
    public string EndPoint { get; }
    public int Port { get; }
    private string? _label;
    internal string Label => _label ??= $"{EndPoint}:{Port}";
    internal RespConnectionManager Manager { get; }

    public Node(RespConnectionManager manager, string endPoint, int port)
    {
        Manager = manager;
        EndPoint = endPoint;
        Port = port;
        _interactive = new(this, false);
    }

    internal object? UserObject { get; set; }
    public bool IsConnected => _interactive.IsConnected;
    public bool IsConnecting => _interactive.IsConnecting;
    public bool IsReplica { get; private set; }

    public void Dispose()
    {
        _isDisposed = true;
        _interactive.Dispose();
        _subscription?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        _isDisposed = true;
        await _interactive.DisposeAsync().ConfigureAwait(false);
        if (_subscription is { } obj)
        {
            await obj.DisposeAsync().ConfigureAwait(false);
        }
    }

    private readonly NodeConnection _interactive;
    private NodeConnection? _subscription;

    public ref readonly RespContext Context => ref _interactive.Context;

    public RespConnection InteractiveConnection => _interactive.Connection;

    public Task<bool> ConnectAsync(
        TextWriter? log = null,
        bool force = false,
        bool pubSub = false)
    {
        if (_isDisposed) return Task.FromResult(false);
        if (!pubSub)
        {
            return _interactive.ConnectAsync(log, force);
        }

        _subscription ??= new(this, pubSub);
        return _subscription.ConnectAsync(log, force);
    }

    public Shard AsShard()
    {
        return new(
            0,
            int.MaxValue,
            Port,
            IsReplica ? ShardFlags.Replica : ShardFlags.None,
            EndPoint,
            "",
            this);
    }
}

internal sealed class NodeConnection : IDisposable, IAsyncDisposable, IRespContextSource
{
    // private EventHandler<RespConnection.RespConnectionErrorEventArgs>? _onConnectionError;
    private readonly Node _node;
    private readonly bool _pubSub;

    public override string ToString() => Label;

    public NodeConnection(Node node, bool pubSub)
    {
        _node = node;
        _pubSub = pubSub;
    }

    private string? _label;
    private string Label => _label ??= _pubSub ? $"{_node.Label}/s" : _node.Label;
    public Node Node => _node;
    private int _state = (int)NodeState.Disconnected;

    private NodeState State => (NodeState)_state;

    private enum NodeState
    {
        Disconnected,
        Connecting,
        Connected,
        Faulted,
        Disposed,
    }

    public bool IsFaulted => State == NodeState.Faulted;
    public bool IsConnected => State == NodeState.Connected;
    public bool IsConnecting => State == NodeState.Connecting;

    public ref readonly RespContext Context => ref _connection.Context;
    private RespConnection _connection = RespContext.Null.Connection;
    public RespConnection Connection => _connection;

    public async Task<bool> ConnectAsync(
        TextWriter? log = null,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        int state;
        bool connecting = false;
        do
        {
            state = _state;
            switch ((NodeState)state)
            {
                case NodeState.Connected when force:
                case NodeState.Connecting when force:
                    log.LogLocked($"[{Label}] (already {(NodeState)state}, but forcing reconnect...)");
                    break; // reconnect anyway!
                case NodeState.Connected:
                case NodeState.Connecting:
                    log.LogLocked($"[{Label}] (already {(NodeState)state})");
                    return true;
                case NodeState.Disposed:
                    log.LogLocked($"[{Label}] (already {(NodeState)state})");
                    return false;
            }
        }
        // otherwise: move to connecting (or retry, if there was a race)
        while (Interlocked.CompareExchange(ref _state, (int)NodeState.Connecting, state) != state);

        try
        {
            // observe outcome of CEX above (noting that if forcing, we don't do that CEX)
            if (State == NodeState.Connecting) state = (int)NodeState.Connecting;

            log.LogLocked($"[{Label}] connecting...");
            connecting = true;
            var manager = _node.Manager;
            var connection = await manager.ConnectionFactory.ConnectAsync(
                _node.EndPoint,
                _node.Port,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            connecting = false;

            log.LogLocked($"[{Label}] Performing handshake...");
            // TODO: handshake

            // finalize the connections
            log.LogLocked($"[{Label}] Finalizing...");
            var oldConnection = _connection;
            _connection = connection.Synchronized();
            await oldConnection.DisposeAsync().ConfigureAwait(false);

            // check nothing changed while we weren't looking
            if (Interlocked.CompareExchange(ref _state, (int)NodeState.Connected, state) == state)
            {
                // success
                log.LogLocked($"[{Label}] (success)");
                /*
                connection.ConnectionError += _onConnectionError ??= OnConnectionError;

                if (state == (int)NodeState.Faulted) OnConnectionRestored();
                */
                return true;
            }

            log.LogLocked($"[{Label}] (unable to complete; became {State})");
            _connection = oldConnection;
            return false;
        }
        catch (Exception ex)
        {
            log.LogLocked($"[{Label}] Faulted: {ex.Message}{(connecting ? " (while connecting)" : "")}");
            // something failed; cleanup and move to faulted, unless disposed
            if (State != NodeState.Disposed)
            {
                _state = (int)NodeState.Faulted;
            }

            var conn = _connection;
            _connection = RespContext.Null.Connection;
            await conn.DisposeAsync();

            /*
            var failureType = ConnectionFailureType.InternalFailure;
            if (connecting)
            {
                failureType = ConnectionFailureType.UnableToConnect;
            }
            else if (ex is SocketException)
            {
                failureType = ConnectionFailureType.SocketFailure;
            }
            else if (ex is ObjectDisposedException)
            {
                failureType = ConnectionFailureType.ConnectionDisposed;
            }

            OnConnectionError(failureType, ex);
            */
            return false;
        }
    }
/*
    private void OnConnectionError(object? sender, RespConnection.RespConnectionErrorEventArgs e)
    {
        var handler = _multiplexer.DirectConnectionFailed;
        if (handler is not null)
        {
            handler(_multiplexer, new ConnectionFailedEventArgs(
                handler,
                _multiplexer,
                _endPoint,
                _connectionType,
                ConnectionFailureType.InternalFailure,
                e.Exception,
                Label));
        }
    }

    private void OnConnectionError(ConnectionFailureType failureType, Exception? exception = null)
    {
        var handler = _multiplexer.DirectConnectionFailed;
        if (handler is not null)
        {
            handler(_multiplexer, new ConnectionFailedEventArgs(
                handler,
                _multiplexer,
                _endPoint,
                _connectionType,
                failureType,
                exception,
                Label));
        }
    }

    private void OnConnectionRestored()
    {
        var handler = _multiplexer.DirectConnectionRestored;
        if (handler is not null)
        {
            handler(_multiplexer, new ConnectionFailedEventArgs(
                handler,
                _multiplexer,
                _endPoint,
                _connectionType,
                ConnectionFailureType.None,
                null,
                Label));
        }
    }*/

    public void Dispose()
    {
        _state = (int)NodeState.Disposed;
        var conn = _connection;
        _connection = RespContext.Null.Connection;
        conn.Dispose();
        // OnConnectionError(ConnectionFailureType.ConnectionDisposed);
    }

    public async ValueTask DisposeAsync()
    {
        _state = (int)NodeState.Disposed;
        var conn = _connection;
        _connection = RespContext.Null.Connection;
        await conn.DisposeAsync().ConfigureAwait(false);
        // OnConnectionError(ConnectionFailureType.ConnectionDisposed);
    }
}
