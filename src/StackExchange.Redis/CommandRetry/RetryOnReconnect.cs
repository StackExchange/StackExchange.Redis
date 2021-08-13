using System;

namespace StackExchange.Redis
{
    /// <summary>
    /// Command retry policy to determine which commands will be retried after a lost connection is retored
    /// </summary>
    public class RetryOnReconnect : IRetryOnReconnectPolicy
    {
        private readonly Func<CommandStatus, bool> _shouldRetry;

        internal RetryOnReconnect(Func<CommandStatus, bool> shouldRetry)
        {
            _shouldRetry = shouldRetry;
        }

        /// <summary>
        /// Retry all commands
        /// </summary>
        /// <returns>An instance of a retry policy that retries all commands</returns>
        public static IRetryOnReconnectPolicy Always
            => new RetryOnReconnect(commandStatus => true);

        /// <summary>
        /// Retry only commands which fail before being sent to the server
        /// </summary>
        /// <returns>An instance of a policy that retries only unsent commands</returns>
        public static IRetryOnReconnectPolicy IfNotSent
            => new RetryOnReconnect(commandStatus => commandStatus == CommandStatus.WaitingToBeSent);

        /// <summary>
        /// Determines whether to retry a command upon restoration of a lost connection
        /// </summary>
        /// <param name="commandStatus">Status of the command</param>
        /// <returns>True to retry the command, otherwise false</returns>
        public bool ShouldRetry(CommandStatus commandStatus)
            => _shouldRetry.Invoke(commandStatus);
    }
}
