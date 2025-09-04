using System.Net;
using System.Net.Sockets;
using StackExchange.Redis;

namespace RESPite.StackExchange.Redis;

internal sealed class Node : IDisposable, IAsyncDisposable, IRespContextProxy
{
    private bool _isDisposed;

    public Version Version { get; }
    public EndPoint EndPoint => _interactive.EndPoint;
    public RespMultiplexer Multiplexer => _interactive.Multiplexer;
    public Node(RespMultiplexer multiplexer, EndPoint endPoint)
    {
        _interactive = new(multiplexer, endPoint, ConnectionType.Interactive);
        Version = multiplexer.Options.DefaultVersion;
        // defer on pub/sub
    }

    public bool IsConnected => _interactive.IsConnected;
    public bool IsConnecting => _interactive.IsConnecting;

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

    public Task<bool> ConnectAsync(TextWriter? log = null, bool force = false, ConnectionType connectionType = ConnectionType.Interactive)
    {
        if (_isDisposed) return Task.FromResult(false);
        if (connectionType == ConnectionType.Interactive)
        {
            return _interactive.ConnectAsync(log, force);
        }
        else if (connectionType == ConnectionType.Subscription)
        {
            _subscription ??= new(_interactive.Multiplexer, _interactive.EndPoint, ConnectionType.Subscription);
            return _subscription.ConnectAsync(log, force);
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(connectionType));
        }
    }

    private IServer? _server;
    public IServer AsServer() => _server ??= new NodeServer(this);
}

internal sealed class NodeConnection : IDisposable, IAsyncDisposable, IRespContextProxy
{
    private EventHandler<RespConnection.RespConnectionErrorEventArgs>? _onConnectionError;
    private readonly RespMultiplexer _multiplexer;
    private readonly EndPoint _endPoint;
    private readonly ConnectionType _connectionType;

    public RespMultiplexer Multiplexer => _multiplexer;

    public NodeConnection(RespMultiplexer multiplexer, EndPoint endPoint, ConnectionType connectionType)
    {
        _multiplexer = multiplexer;
        _endPoint = endPoint;
        _connectionType = connectionType;
        _label = Format.ToString(endPoint);
    }

    public EndPoint EndPoint => _endPoint;
    private int _state = (int)NodeState.Disconnected;
    private readonly string _label;

    public override string ToString() => $"{_label}: {State}";
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

    public async Task<bool> ConnectAsync(TextWriter? log = null, bool force = false)
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
                    log.LogLocked($"[{_label}] (already {(NodeState)state}, but forcing reconnect...)");
                    break; // reconnect anyway!
                case NodeState.Connected:
                case NodeState.Connecting:
                    log.LogLocked($"[{_label}] (already {(NodeState)state})");
                    return true;
                case NodeState.Disposed:
                    log.LogLocked($"[{_label}] (already {(NodeState)state})");
                    return false;
            }
        }
        // otherwise: move to connecting (or retry, if there was a race)
        while (Interlocked.CompareExchange(ref _state, (int)NodeState.Connecting, state) != state);

        try
        {
            log.LogLocked($"[{_label}] Connecting...");
            connecting = true;
            var connection = await RespConnection.CreateAsync(
                _endPoint,
                cancellationToken: _multiplexer.Lifetime).ConfigureAwait(false);
            connecting = false;

            log.LogLocked($"[{_label}] Performing handshake...");
            // TODO: handshake

            // finalize the connections
            log.LogLocked($"[{_label}] Finalizing...");
            var oldConnection = _connection;
            _connection = connection;
            await oldConnection.DisposeAsync().ConfigureAwait(false);

            // check nothing changed while we weren't looking
            if (Interlocked.CompareExchange(ref _state, (int)NodeState.Connected, state) == state)
            {
                // success
                log.LogLocked($"[{_label}] (success)");
                connection.ConnectionError += _onConnectionError ??= OnConnectionError;

                if (state == (int)NodeState.Faulted) OnConnectionRestored();
                return true;
            }

            log.LogLocked($"[{_label}] (unable to complete; became {State})");
            _connection = oldConnection;
            return false;
        }
        catch (Exception ex)
        {
            log.LogLocked($"[{_label}] Faulted: {ex.Message}");
            // something failed; cleanup and move to faulted, unless disposed
            if (State != NodeState.Disposed)
            {
                _state = (int)NodeState.Faulted;
            }

            var conn = _connection;
            _connection = RespContext.Null.Connection;
            await conn.DisposeAsync();

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
            return false;
        }
    }

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
                _label));
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
                _label));
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
                _label));
        }
    }

    public void Dispose()
    {
        _state = (int)NodeState.Disposed;
        var conn = _connection;
        _connection = RespContext.Null.Connection;
        conn.Dispose();
        OnConnectionError(ConnectionFailureType.ConnectionDisposed);
    }

    public async ValueTask DisposeAsync()
    {
        _state = (int)NodeState.Disposed;
        var conn = _connection;
        _connection = RespContext.Null.Connection;
        await conn.DisposeAsync().ConfigureAwait(false);
        OnConnectionError(ConnectionFailureType.ConnectionDisposed);
    }
}
