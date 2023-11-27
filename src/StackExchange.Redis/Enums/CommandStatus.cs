namespace StackExchange.Redis
{
    /// <summary>
    /// Track status of a command while communicating with Redis.
    /// </summary>
    public enum CommandStatus
    {
        /// <summary>
        /// Command status unknown.
        /// </summary>
        Unknown,
        /// <summary>
        /// ConnectionMultiplexer has not yet started writing this command to Redis.
        /// </summary>
        WaitingToBeSent,
        /// <summary>
        /// Command has been sent to Redis.
        /// </summary>
        Sent,
        /// <summary>
        /// Command is in the backlog, waiting to be processed and written to Redis.
        /// </summary>
        WaitingInBacklog,
    }
}
