namespace StackExchange.Redis
{
    /// <summary>
    /// The reason a command failed to send or complete.
    /// </summary>
    public enum CommandFailureReason
    {
        /// <summary>
        /// No open/valid connection was avaialble to send on - we couldn't even write the command.
        /// </summary>
        WriteFailure,
        /// <summary>
        /// The message was sent, but we lost the connection and this command in-flight.
        /// </summary>
        ConnectionFailure,
        /// <summary>
        /// Command has timed out, exceeding the sync or async timeout limits
        /// </summary>
        Timeout,
        /// <summary>
        /// This command failed again, during a retry
        /// </summary>
        RetryFailure,
    }
}
