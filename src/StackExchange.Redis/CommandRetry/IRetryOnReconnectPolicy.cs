namespace StackExchange.Redis
{
    /// <summary>
    /// Interface for a policy that determines which commands should be retried upon restoration of a lost connection
    /// </summary>
    public interface IRetryOnReconnectPolicy
    {
        /// <summary>
        /// Determines whether a failed command should be retried
        /// </summary>
        /// <param name="commandStatus">Current state of the command</param>
        /// <returns>True to retry the command, otherwise false</returns>
        public bool ShouldRetry(CommandStatus commandStatus);
    }
}
