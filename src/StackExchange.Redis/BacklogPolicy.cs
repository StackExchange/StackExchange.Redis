namespace StackExchange.Redis
{
    /// <summary>
    /// The backlog policy to use for commands. This policy comes into effect when a connection is unhealthy or unavailable.
    /// The policy can choose to backlog commands and wait to try them (within their timeout) against a connection when it comes up,
    /// or it could choose to fail fast and throw ASAP. Different apps desire different behaviors with backpressure and how to handle
    /// large amounts of load, so this is configurable to optimize the happy path but avoid spiral-of-death queue scenarios for others.
    /// </summary>
    public sealed class BacklogPolicy
    {
        /// <summary>
        /// Backlog behavior matching StackExchange.Redis's 2.x line, failing fast and not attempting to queue
        /// and retry when a connection is available again.
        /// </summary>
        public static BacklogPolicy FailFast { get; } = new()
        {
            QueueWhileDisconnected = false,
            AbortPendingOnConnectionFailure = true,
        };

        /// <summary>
        /// Default backlog policy which will allow commands to be issues against an endpoint and queue up.
        /// Commands are still subject to their async timeout (which serves as a queue size check).
        /// </summary>
        public static BacklogPolicy Default { get; } = new()
        {
            QueueWhileDisconnected = true,
            AbortPendingOnConnectionFailure = false,
        };

        /// <summary>
        /// Whether to queue commands while disconnected.
        /// True means queue for attempts up until their timeout.
        /// <see langword="false"/> means to fail ASAP and queue nothing.
        /// </summary>
        public bool QueueWhileDisconnected { get; init; }

        /// <summary>
        /// Whether to immediately abandon (with an exception) all pending commands when a connection goes unhealthy.
        /// </summary>
        public bool AbortPendingOnConnectionFailure { get; init; }
    }
}
