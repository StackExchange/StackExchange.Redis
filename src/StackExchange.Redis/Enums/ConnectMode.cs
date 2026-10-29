namespace StackExchange.Redis
{
    /// <summary>
    /// How eagerly connections to the configured endpoints are opened.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is a genuine trade rather than a default with a workaround.</b> A caller who places keys
    /// deliberately - hash tags, a shard per tenant, a keyspace partitioned on purpose - touches a small
    /// and known part of a large deployment, and opening a socket to every node of it is cost with no
    /// return. A caller treating the keyspace as opaque reaches everything eventually, so opening
    /// everything up front costs them nothing and answers every question immediately.
    /// </para>
    /// <para>
    /// The questions in play are the ones answered WITHOUT sending a command: whether the client is
    /// connected, and which endpoint serves a given key. A client that has not dialled cannot answer them
    /// from observation, only from configuration.
    /// </para>
    /// </remarks>
    internal enum ConnectMode
    {
        /// <summary>
        /// Open nothing until a command needs it.
        /// </summary>
        /// <remarks>
        /// Cheapest for a deployment far larger than the part of it this client uses; in exchange,
        /// questions asked before the first command are answered from configuration rather than from a
        /// connection.
        /// </remarks>
        Lazy = 0,

        /// <summary>
        /// Open one connection while connecting, and the rest on demand.
        /// </summary>
        /// <remarks>
        /// The middle, and usually the right one: the configured endpoints need no discovering, and a
        /// single handshake describes the whole deployment - <c>CLUSTER SLOTS</c> names every node,
        /// <c>ROLE</c> names the other side of a replication pair. So the topology is fully known having
        /// opened one socket, and a hundred-node cluster still opens one.
        /// </remarks>
        Discover = 1,

        /// <summary>
        /// Open a connection to every configured endpoint while connecting.
        /// </summary>
        /// <remarks>The historical behaviour, and the one a keyspace-opaque caller wants.</remarks>
        Eager = 2,
    }
}
