using System;

namespace StackExchange.Redis
{
    /// <summary>
    /// Policy that determines which commands should be retried upon restoration of a lost connection.
    /// </summary>
    public interface ICommandRetryPolicy
    {
        internal void Init(ConnectionMultiplexer mux);
        internal bool TryQueue(Message message, Exception ex);

        /// <summary>
        /// Returns the current length of the retry queue.
        /// </summary>
        public int CurrentQueueLength { get; }

        /// <summary>
        /// Determines whether a failed command should be retried.
        /// </summary>
        /// <param name="commandStatus">Current state of the command.</param>
        /// <returns>True to retry the command, otherwise false.</returns>
        public bool ShouldRetry(CommandStatus commandStatus);

        /// <summary>
        /// Called when a heartbeat occurs.
        /// </summary>
        void OnHeartbeat();

        /// <summary>
        /// Called when a multiplexer reconnects.
        /// </summary>
        void OnReconnect();
    }
}
