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
        protected CommandRetryPolicy(ConnectionMultiplexer muxer) { }

        /// <summary>
        /// Returns the current length of the retry queue.
        /// </summary>
        public abstract int CurrentQueueLength { get; }

        /// <summary>
        /// Returns whether the current queue is processing (e.g. retrying queued commands).
        /// </summary>
        public abstract bool CurrentlyProcessing { get; }

        /// <summary>
        /// Returns the status of the retry mechanism, e.g. what the queue is doing.
        /// </summary>
        public abstract string StatusDescription { get; }

        /// <summary>
        /// Determines if a message is eligible for retrying at all.
        /// </summary>
        /// <param name="message">The message to check eligibility for.</param>
        /// <returns>True if a message is eligible.</returns>
        internal static bool IsEligible(Message message)
        {
            if ((message.Flags & CommandFlags.NoRetry) != 0
                || ((message.Flags & CommandFlags.RetryIfNotSent) != 0 && message.Status == CommandStatus.Sent)
                || message.IsAdmin
                || message.IsInternalCall)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Determines if an xception is eligible for retrying at all.
        /// </summary>
        /// <param name="exception">The exception to check eligibility for.</param>
        /// <returns>True if an exception is eligible.</returns>
        internal static bool IsEligible(Exception exception) => exception is RedisException;

        /// <summary>
        /// Tries to queue an eligible command.
        /// Protected because this isn't called directly - eligibility (above) is checked first by the multiplexer.
        /// </summary>
        protected internal abstract bool TryQueue(FailedCommand command);

        /// <summary>
        /// Called when a heartbeat occurs.
        /// </summary>
        public abstract void OnHeartbeat();

        /// <summary>
        /// Called when a multiplexer reconnects.
        /// </summary>
        public abstract void OnReconnect();

        /// <summary>
        /// Default policy - retry only commands which fail before being sent to the server (alias for <see cref="IfNotSent"/>).
        /// </summary>
        /// <returns>An instance of a policy that retries only unsent commands.</returns>
        public static Func<ConnectionMultiplexer, CommandRetryPolicy> Default => IfNotSent;

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
            => mutex => new DefaultCommandRetryPolicy(mutex, command => command.Status == CommandStatus.WaitingToBeSent);

        /// <summary>
        /// Never retry a command.
        /// </summary>
        /// <returns>An instance of a retry policy that retries no commands.</returns>
        public static Func<ConnectionMultiplexer, CommandRetryPolicy> Never
            => mutex => new NeverCommandRetryPolicy(mutex);
    }
}
