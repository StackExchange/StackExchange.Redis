using System;
using System.Net;
using System.Runtime.CompilerServices;
using StackExchange.Redis.Maintenance;

namespace StackExchange.Redis;

public partial class ConnectionMultiplexer
{
    /// <summary>
    /// Raised whenever a physical connection fails.
    /// </summary>
    public event EventHandler<ConnectionFailedEventArgs>? ConnectionFailed;
    internal void OnConnectionFailed(EndPoint endpoint, ConnectionType connectionType, ConnectionFailureType failureType, Exception exception, bool reconfigure, string? physicalName)
    {
        if (_isDisposed) return;
        var handler = ConnectionFailed;
        if (handler != null)
        {
            CompleteAsWorker(new ConnectionFailedEventArgs(handler, this, endpoint, connectionType, failureType, exception, physicalName));
        }
        if (reconfigure)
        {
            ReconfigureIfNeeded(endpoint, false, "connection failed");
        }
    }

    /// <summary>
    /// Raised whenever an internal error occurs (this is primarily for debugging).
    /// </summary>
    public event EventHandler<InternalErrorEventArgs>? InternalError;
    internal void OnInternalError(Exception exception, EndPoint? endpoint = null, ConnectionType connectionType = ConnectionType.None, [CallerMemberName] string? origin = null)
    {
        try
        {
            if (_isDisposed) return;
            Trace("Internal error: " + origin + ", " + exception == null ? "unknown" : exception.Message);
            var handler = InternalError;
            if (handler != null)
            {
                CompleteAsWorker(new InternalErrorEventArgs(handler, this, endpoint, connectionType, exception, origin));
            }
        }
        catch
        {
            // Our internal error event failed...whatcha gonna do, exactly?
        }
    }

    /// <summary>
    /// Raised whenever a physical connection is established.
    /// </summary>
    public event EventHandler<ConnectionFailedEventArgs>? ConnectionRestored;
    internal void OnConnectionRestored(EndPoint endpoint, ConnectionType connectionType, string? physicalName)
    {
        if (_isDisposed) return;
        var handler = ConnectionRestored;
        if (handler != null)
        {
            CompleteAsWorker(new ConnectionFailedEventArgs(handler, this, endpoint, connectionType, ConnectionFailureType.None, null, physicalName));
        }
        ReconfigureIfNeeded(endpoint, false, "connection restored");
    }

    /// <summary>
    /// Raised when configuration changes are detected.
    /// </summary>
    public event EventHandler<EndPointEventArgs>? ConfigurationChanged;
    internal void OnConfigurationChanged(EndPoint endpoint) => OnEndpointChanged(endpoint, ConfigurationChanged);

    /// <summary>
    /// Raised when nodes are explicitly requested to reconfigure via broadcast.
    /// This usually means primary/replica changes.
    /// </summary>
    public event EventHandler<EndPointEventArgs>? ConfigurationChangedBroadcast;
    internal void OnConfigurationChangedBroadcast(EndPoint endpoint) => OnEndpointChanged(endpoint, ConfigurationChangedBroadcast);

    private void OnEndpointChanged(EndPoint endpoint, EventHandler<EndPointEventArgs>? handler)
    {
        if (_isDisposed) return;
        if (handler != null)
        {
            CompleteAsWorker(new EndPointEventArgs(handler, this, endpoint));
        }
    }

    /// <summary>
    /// Raised when server indicates a maintenance event is going to happen.
    /// </summary>
    public event EventHandler<ServerMaintenanceEvent>? ServerMaintenanceEvent;
    internal void OnServerMaintenanceEvent(ServerMaintenanceEvent e) =>
        ServerMaintenanceEvent?.Invoke(this, e);

    /// <summary>
    /// Raised when a hash-slot has been relocated.
    /// </summary>
    public event EventHandler<HashSlotMovedEventArgs>? HashSlotMoved;
    internal void OnHashSlotMoved(int hashSlot, EndPoint? old, EndPoint @new)
    {
        var handler = HashSlotMoved;
        if (handler != null)
        {
            CompleteAsWorker(new HashSlotMovedEventArgs(handler, this, hashSlot, old, @new));
        }
    }

    /// <summary>
    /// Raised when a server replied with an error message.
    /// </summary>
    public event EventHandler<RedisErrorEventArgs>? ErrorMessage;
    internal void OnErrorMessage(EndPoint endpoint, string message)
    {
        if (_isDisposed) return;
        var handler = ErrorMessage;
        if (handler != null)
        {
            CompleteAsWorker(new RedisErrorEventArgs(handler, this, endpoint, message));
        }
    }
}
