using System;

namespace StackExchange.Redis
{
    /// <summary>
    /// Policy that determines which commands should be retried upon restoration of a lost connection.
    /// </summary>
    public abstract class CommandRetryPolicy
    {
        /// <summary>
        /// Creates a policy instance for a specific multiplexer and its commands.
        /// </summary>
        /// <param name="muxer">The muleiplexer this policy is for.</param>
        protected CommandRetryPolicy(ConnectionMultiplexer muxer)
        {
        }

        internal abstract bool TryQueue(Message message, Exception ex);

        /// <summary>
        /// Returns the current length of the retry queue.
        /// </summary>
        public abstract int CurrentQueueLength { get; }

        /// <summary>
        /// Determines whether a failed command should be retried.
        /// </summary>
        /// <param name="commandStatus">Current state of the command.</param>
        /// <returns>True to retry the command, otherwise false.</returns>
        public abstract bool ShouldRetry(CommandStatus commandStatus);

        /// <summary>
        /// Called when a heartbeat occurs.
        /// </summary>
        public abstract void OnHeartbeat();

        /// <summary>
        /// Called when a multiplexer reconnects.
        /// </summary>
        public abstract void OnReconnect();

        /// <summary>
        /// Retry all commands.
        /// </summary>
        /// <returns>An instance of a retry policy that retries all commands.</returns>
        public static Func<ConnectionMultiplexer, CommandRetryPolicy> Always
            => mutex => new DefaultCommandRetryPolicy(mutex, commandStatus => true);

        /// <summary>
        /// Retry only commands which fail before being sent to the server.
        /// </summary>
        /// <returns>An instance of a policy that retries only unsent commands.</returns>
        public static Func<ConnectionMultiplexer, CommandRetryPolicy> IfNotSent
            => mutex => new DefaultCommandRetryPolicy(mutex, commandStatus => commandStatus == CommandStatus.WaitingToBeSent);
    }
}
