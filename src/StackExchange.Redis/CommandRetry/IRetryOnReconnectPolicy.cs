using System;

namespace StackExchange.Redis
{
    /// <summary>
    /// Interface for a policy that determines which commands should be retried upon restoration of a lost connection
    /// </summary>
    public interface IRetryOnReconnectPolicy
    {
        internal MessageRetryQueue RetryQueueManager { get; set;}
        internal void Init(ConnectionMultiplexer mux);
        internal bool TryMessageForRetry(Message message, Exception ex);

        /// <summary>
        /// returns the current length of the retry queue
        /// </summary>
        public int RetryQueueCurrentLength { get; }


        /// <summary>
        /// Determines whether a failed command should be retried
        /// </summary>
        /// <param name="commandStatus">Current state of the command</param>
        /// <returns>True to retry the command, otherwise false</returns>
        public bool ShouldRetry(CommandStatus commandStatus);
    }
}
