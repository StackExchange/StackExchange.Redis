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
        /// ConnectionMultiplexer has not yet started writing this command to redis.
        /// </summary>
        WaitingToBeSent,
        /// <summary>
        /// Command has been sent to Redis.
        /// </summary>
        Sent,
    }
}
