using System.Diagnostics.CodeAnalysis;
using RESPite;

namespace StackExchange.Redis
{
    /// <summary>
    /// The known types of connection failure.
    /// </summary>
    public enum ConnectionFailureType
    {
        /// <summary>
        /// This event is not a failure.
        /// </summary>
        None,

        /// <summary>
        /// No viable connections were available for this operation.
        /// </summary>
        UnableToResolvePhysicalConnection,

        /// <summary>
        /// The socket for this connection failed.
        /// </summary>
        SocketFailure,

        /// <summary>
        /// Either SSL Stream or Redis authentication failed.
        /// </summary>
        AuthenticationFailure,

        /// <summary>
        /// An unexpected response was received from the server.
        /// </summary>
        ProtocolFailure,

        /// <summary>
        /// An unknown internal error occurred.
        /// </summary>
        InternalFailure,

        /// <summary>
        /// The socket was closed.
        /// </summary>
        SocketClosed,

        /// <summary>
        /// The socket was closed.
        /// </summary>
        ConnectionDisposed,

        /// <summary>
        /// The database is loading and is not available for use.
        /// </summary>
        Loading,

        /// <summary>
        /// It has not been possible to create an initial connection to the redis server(s).
        /// </summary>
        UnableToConnect,

        /// <summary>
        /// High-integrity mode was enabled, and a failure was detected.
        /// </summary>
        ResponseIntegrityFailure,

        /// <summary>
        /// The <see cref="CircuitBreaker"/> associated with this connection detected instability.
        /// </summary>
        [Experimental(Experiments.GeoRedundantFailover, UrlFormat = Experiments.UrlFormat)]
        CircuitBreaker,

        /// <summary>
        /// The connection was replaced deliberately, to move off an endpoint the server announced it is
        /// retiring.
        /// </summary>
        /// <remarks>
        /// Not a fault: the connection was working, and we chose to replace it while a replacement address was
        /// known rather than wait to be disconnected. Reported here because the alternative is worse - the
        /// replacement raises <see cref="ConnectionMultiplexer.ConnectionRestored"/>, so without this a consumer
        /// tracking connection state sees a restore with no matching failure, and no reason for the churn.
        /// <para>
        /// Consumers alerting on <see cref="ConnectionMultiplexer.ConnectionFailed"/> should filter this out:
        /// it is expected during planned maintenance and says nothing is wrong. <see cref="CircuitBreaker"/> is
        /// the precedent for a deliberate client action being reported this way.
        /// </para>
        /// </remarks>
        MaintenanceHandoff,
    }
}
