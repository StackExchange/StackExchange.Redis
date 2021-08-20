using System;

namespace StackExchange.Redis
{
    /// <summary>
    /// Command retry policy to determine which commands will be retried after a lost connection is retored
    /// </summary>
    public class DefaultCommandRetryPolicy : ICommandRetryPolicy
    {
        private MessageRetryQueue RetryQueue { get; set; }
        private readonly Func<CommandStatus, bool> _shouldRetry;

        internal DefaultCommandRetryPolicy(Func<CommandStatus, bool> shouldRetry)
        {
            _shouldRetry = shouldRetry;
        }

        /// <summary>
        /// Gets whether this message is eligible for retrying according to this policy.
        /// </summary>
        /// <param name="message">The message to retry.</param>
        /// <param name="ex">The exception from the initial send.</param>
        /// <returns>Whether the given message/exception combination is eligible for retry.</returns>
        internal bool IsMessageRetriable(Message message, Exception ex)
        {
            if ((message.Flags & CommandFlags.NoRetry) != 0
                || ((message.Flags & CommandFlags.RetryIfNotSent) != 0 && message.Status == CommandStatus.Sent)
                || message.IsAdmin
                || message.IsInternalCall
                || !(ex is RedisException)
                || !ShouldRetry(message.Status))
                return false;

            return true;
        }

        void ICommandRetryPolicy.Init(ConnectionMultiplexer mux)
        {
            var messageRetryHelper = new MessageRetryHelper(mux);
            RetryQueue = new MessageRetryQueue(messageRetryHelper);
        }

        bool ICommandRetryPolicy.TryQueue(Message message, Exception ex)
        {
            if (!IsMessageRetriable(message, ex))
            {
                return false;
            }

            if (RetryQueue.TryHandleFailedCommand(message))
            {
                // if this message is a new message set the writetime
                if (message.GetWriteTime() == 0)
                {
                    message.SetEnqueued(null);
                }

                message.ResetStatusToWaitingToBeSent();

                return true;
            }

            return false;
        }

        /// <summary>
        /// Called on heartbeat, evaluating if anything in queue has timed out and need pruning.
        /// </summary>
        public void OnHeartbeat() => RetryQueue?.CheckRetryQueueForTimeouts();

        /// <summary>
        /// Called on a multiplexer reconnect, to start sending anything in the queue.
        /// </summary>
        public void OnReconnect() => RetryQueue?.StartRetryQueueProcessor();

        /// <summary>
        /// Retry all commands.
        /// </summary>
        /// <returns>An instance of a retry policy that retries all commands.</returns>
        public static ICommandRetryPolicy Always
            => new DefaultCommandRetryPolicy(commandStatus => true);

        /// <summary>
        /// Retry only commands which fail before being sent to the server.
        /// </summary>
        /// <returns>An instance of a policy that retries only unsent commands.</returns>
        public static ICommandRetryPolicy IfNotSent
            => new DefaultCommandRetryPolicy(commandStatus => commandStatus == CommandStatus.WaitingToBeSent);

        /// <summary>
        /// Gets the current length of the retry queue.
        /// </summary>
        public int CurrentQueueLength => RetryQueue.CurrentRetryQueueLength;

        /// <summary>
        /// Determines whether to retry a command upon restoration of a lost connection.
        /// </summary>
        /// <param name="commandStatus">Status of the command.</param>
        /// <returns>True to retry the command, otherwise false.</returns>
        public bool ShouldRetry(CommandStatus commandStatus)
            => _shouldRetry.Invoke(commandStatus);
    }
}
