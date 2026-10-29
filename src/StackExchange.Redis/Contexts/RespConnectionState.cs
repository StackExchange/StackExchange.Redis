namespace StackExchange.Redis
{
    /// <summary>
    /// What a routing question can say about reaching a server, beyond yes and no.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A boolean was enough while connections were eager and is not now.</b> The shipped core dials
    /// every endpoint up front, so "no connection" could only mean something was wrong. This core dials on
    /// demand, so the common answer for a healthy server is "nothing has needed it yet" - which a boolean
    /// reports as <c>false</c>, indistinguishable from a server that is refusing.
    /// </para>
    /// <para>
    /// Ordered from worst to best, so aggregating several endpoints is <c>Max</c>.
    /// </para>
    /// </remarks>
    internal enum RespConnectionState : byte
    {
        /// <summary>Nothing would serve this at all - no endpoint matches the key, the role, or both.</summary>
        /// <remarks>
        /// Distinct from <see cref="Deferred"/> on purpose, and the distinction is the reason this type
        /// exists: <c>DemandReplica</c> against a primary with no replicas is unroutable however long you
        /// wait, while a deferred endpoint only needs somebody to ask.
        /// </remarks>
        Unroutable = 0,

        /// <summary>
        /// An endpoint would serve it, and this core has not dialled it because nothing has needed it.
        /// </summary>
        /// <remarks>
        /// <b>Not a fault.</b> There is nothing to fix and nothing to wait for; the connection is made when
        /// a command is sent. This is the state a boolean could not express.
        /// </remarks>
        Deferred,

        /// <summary>A connection attempt is in flight, or the last one failed and the backoff is deciding.</summary>
        Connecting,

        /// <summary>There is a live connection to the endpoint that would serve this.</summary>
        Connected,
    }
}
