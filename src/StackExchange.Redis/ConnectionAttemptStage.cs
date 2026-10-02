namespace StackExchange.Redis;

/// <summary>
/// How far a physical connection attempt got; see <see cref="ConnectionAttemptCompletedEventArgs.Stage"/>.
/// </summary>
/// <remarks>Values are spaced apart, so that finer-grained stages can be added later without renumbering; compare stages by order, not equality, where "at least as far as" is meant.</remarks>
public enum ConnectionAttemptStage
{
    /// <summary>
    /// The stage is not known.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// Establishing the connection: resolving and connecting the socket, or (when a tunnel supplies the entire transport,
    /// including any TLS) creating that transport.
    /// </summary>
    Connect = 100,

    /// <summary>
    /// Pre-authentication negotiation by the configured <see cref="Configuration.Tunnel"/> (for example, an HTTP proxy <c>CONNECT</c>).
    /// </summary>
    Tunnel = 200,

    /// <summary>
    /// The TLS handshake performed by the library.
    /// </summary>
    Tls = 300,

    /// <summary>
    /// The Redis handshake (for example <c>HELLO</c>, <c>AUTH</c>, and the initial configuration queries). Note that with TLS 1.3, a
    /// client certificate rejected by the server is typically observed here, rather than during <see cref="Tls"/>.
    /// </summary>
    Handshake = 400,

    /// <summary>
    /// The attempt completed successfully.
    /// </summary>
    Established = 500,
}
