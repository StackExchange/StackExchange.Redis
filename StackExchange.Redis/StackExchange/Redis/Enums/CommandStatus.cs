namespace StackExchange.Redis
{
    /// <summary>
    /// track status of a command while communicating with Redis
    /// </summary>
    public enum CommandStatus
    {
        /// <summary>
        /// command status unknown
        /// </summary>
        Unknown,
        /// <summary>
        /// ConnectionMultiplexer has not yet started writing this command to redis 
        /// </summary>
        WaitingToBeSent,
        /// <summary>
        /// command has been sent to Redis
        /// </summary>
        Sent,
    }
}
