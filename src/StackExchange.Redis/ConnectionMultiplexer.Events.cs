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

    /// <summary>
    /// How host names are resolved during a maintenance handoff.
    /// </summary>
    /// <remarks>
    /// A seam rather than a call to <see cref="System.Net.Dns"/> directly, because no in-process fake can move a
    /// DNS record: the handoff's whole decision turns on the answer *changing*, and the only way to test that
    /// deterministically is to supply the answers. Defaults to real DNS.
    /// </remarks>
    internal Func<string, System.Threading.CancellationToken, System.Threading.Tasks.Task<System.Net.IPAddress[]>> AddressResolver { get; set; }
        = Maintenance.AdvertisedAddressProbe.DefaultResolveAsync;

    // recently-raised (type, sequence) pairs, so one logical event raises one event however many nodes told
    // us. Small and fixed: the copies arrive within milliseconds of each other, so a handful of slots covers
    // any realistic proxy count even with other notifications interleaved.
    // A named struct rather than a tuple: this assembly must not reference System.ValueTuple, which breaks
    // .NET Framework consumers - see SanityChecks.ValueTupleNotReferenced.
    private readonly struct RaisedMaintenanceEvent(Maintenance.MaintenanceNotificationType type, long sequence)
    {
        public readonly Maintenance.MaintenanceNotificationType Type = type;
        public readonly long Sequence = sequence;
    }

    private readonly RaisedMaintenanceEvent[] _raisedMaintenanceEvents = new RaisedMaintenanceEvent[8];
    private int _raisedMaintenanceEventIndex;

    /// <summary>
    /// Whether this is the first time we have been told about a given maintenance event, across every
    /// connection.
    /// </summary>
    /// <remarks>
    /// Every node broadcasts a given event, and all of them carry the same sequence number - observed on
    /// Enterprise 8.6.2, where the id identifies the event rather than the delivery. So without this, a
    /// deployment fronted by three proxies raises three events for one migration and every consumer has to
    /// dedupe them.
    /// <para>
    /// Matched on equality rather than "less than or equal", deliberately: a lagging node reporting an
    /// *earlier* event we have not seen yet is a distinct event and must still be raised. Only an exact repeat
    /// of something already raised is a duplicate.
    /// </para>
    /// <para>
    /// Eviction is the only expiry - an entry falls out once eight further notifications have been recorded, so
    /// nothing has to be purged on a timer and the state cannot grow. A duplicate arriving after its entry has
    /// been evicted would be raised a second time, which is the right way round to be wrong: the copies arrive
    /// within milliseconds of each other, so that takes a straggler behind eight intervening events.
    /// </para>
    /// </remarks>
    internal bool TryClaimMaintenanceEvent(Maintenance.MaintenanceNotificationType type, long? sequence)
    {
        if (sequence is not { } seq) return true; // no id to match on; better a duplicate than a silence

        lock (_raisedMaintenanceEvents)
        {
            foreach (var entry in _raisedMaintenanceEvents)
            {
                if (entry.Type == type && entry.Sequence == seq) return false;
            }

            _raisedMaintenanceEvents[_raisedMaintenanceEventIndex] = new RaisedMaintenanceEvent(type, seq);
            _raisedMaintenanceEventIndex = (_raisedMaintenanceEventIndex + 1) % _raisedMaintenanceEvents.Length;
            return true;
        }
    }
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
