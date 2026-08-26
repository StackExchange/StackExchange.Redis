using System.Diagnostics.CodeAnalysis;
using RESPite;

namespace StackExchange.Redis;

internal sealed partial class ServerEndPoint
{
    private volatile bool _maintenanceNotificationsActive, _maintenanceNotificationsRequested;
    private volatile string? _maintenanceNotificationsRefusal;

    /// <summary>
    /// Whether this server has accepted our request for maintenance notifications on the current connection.
    /// </summary>
    /// <remarks>
    /// Per-connection state, so this is cleared at the start of every handshake and re-established from the
    /// reply; the scope is deliberately the server rather than the multiplexer, so one lagging node doesn't
    /// disable the feature for the whole deployment.
    /// </remarks>
    internal bool MaintenanceNotificationsActive => _maintenanceNotificationsActive;

    [Experimental(Experiments.MaintenanceNotifications, UrlFormat = Experiments.UrlFormat)]
    private MaintenanceNotificationMode MaintenanceMode
        => Multiplexer.RawConfig.MaintenanceNotifications;

    /// <summary>
    /// Whether to ask this server for maintenance notifications during handshake.
    /// </summary>
    /// <remarks>
    /// The feature is RESP3-only: the notifications are out-of-band push frames on the connection that
    /// carries commands, and a RESP2 connection can have the request accepted and then silently receive
    /// nothing - so we don't ask unless we asked for RESP3. (Note that not asking is not the same as being
    /// satisfied: under <see cref="MaintenanceNotificationMode.Enabled"/> the reconcile below fails the
    /// connection for exactly this case.) We can't know what was *negotiated* at write time
    /// (the handshake is pipelined, and <c>HELLO</c> hasn't been answered yet), so this tests what we asked
    /// for and <see cref="ReconcileMaintenanceNotifications"/> settles it once the reply has been processed.
    /// </remarks>
    private bool ShouldRequestMaintenanceNotifications(bool isInteractive, bool negotiateResp3)
        => isInteractive
        && negotiateResp3
        && MaintenanceMode != MaintenanceNotificationMode.Disabled
        && Multiplexer.CommandMap.IsAvailable(RedisCommand.CLIENT);

    internal void OnMaintenanceNotificationsAccepted() => _maintenanceNotificationsActive = true;

    /// <summary>
    /// The server declined our request. Recorded rather than acted on: whether that matters is a question for
    /// <see cref="ReconcileMaintenanceNotifications"/>, which sees the negotiated protocol too.
    /// </summary>
    internal void OnMaintenanceNotificationsRefused(PhysicalConnection connection, string reason)
    {
        _maintenanceNotificationsActive = false;
        _maintenanceNotificationsRefusal = reason;
        connection.OnDetailLog($"maintenance notifications refused: {reason}");
    }

    /// <summary>
    /// Settles the feature for this connection now that the handshake is complete and the protocol is known.
    /// </summary>
    /// <remarks>
    /// The opt-in reply precedes the tracer on the same pipelined connection, so by the time this runs every
    /// fact is in: whether we asked, what the server said, and what protocol we ended up on.
    /// </remarks>
    private void ReconcileMaintenanceNotifications(PhysicalConnection connection)
    {
        bool resp3 = connection.Protocol is >= RedisProtocol.Resp3;
        if (!resp3)
        {
            // whatever the server said about the opt-in, nothing can arrive on a RESP2 connection
            _maintenanceNotificationsActive = false;
        }

        if (_maintenanceNotificationsActive || MaintenanceMode != MaintenanceNotificationMode.Enabled)
        {
            return;
        }

        // Enabled means required: no notifications, no connection. That includes a configuration that never
        // got as far as asking - requiring a RESP3-only feature over RESP2 is a contradiction, and failing it
        // is more useful than honouring half of it. Note the cross-client spec only calls for failing when
        // the *server* errors; extending that to the RESP2 cases is ours, and deliberate
        var reason = !resp3
            ? (_maintenanceNotificationsRequested ? "the connection negotiated RESP2" : "RESP3 was not requested")
            : _maintenanceNotificationsRefusal ?? "the server did not accept the request";

        connection.RecordConnectionFailed(
            ConnectionFailureType.ProtocolFailure,
            new RedisConnectionException(ConnectionFailureType.ProtocolFailure, CommandFlags.None, $"Maintenance notifications are enabled, but unavailable: {reason}", innerException: null));
    }
}
