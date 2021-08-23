using System;

namespace StackExchange.Redis
{
    /// <summary>
    /// Command retry policy to determine which commands will be retried after a lost connection is retored
    /// </summary>
    public class DefaultCommandRetryPolicy : CommandRetryPolicy
    {
        private MessageRetryQueue RetryQueue { get; }

        private readonly Func<CommandStatus, bool> _shouldRetry;

        /// <summary>
        /// Creates a <see cref="DefaultCommandRetryPolicy"/> for the given <see cref="ConnectionMultiplexer"/>.
        /// </summary>
        /// <param name="muxer">The <see cref="ConnectionMultiplexer"/> to handle retries for.</param>
        /// <param name="shouldRetry">Whether a command should be retried.</param>
        internal DefaultCommandRetryPolicy(ConnectionMultiplexer muxer, Func<CommandStatus, bool> shouldRetry) : base(muxer)
        {
            _shouldRetry = shouldRetry;
            var messageRetryHelper = new MessageRetryHelper(muxer);
            RetryQueue = new MessageRetryQueue(messageRetryHelper);
        }

        /// <summary>
        /// Gets whether this message is eligible for retrying according to this policy.
        /// </summary>
        /// <param name="message">The message to retry.</param>
        /// <param name="ex">The exception from the initial send.</param>
        /// <returns>Whether the given message/exception combination is eligible for retry.</returns>
        internal bool CanRetry(Message message, Exception ex)
        {
            if ((message.Flags & CommandFlags.NoRetry) != 0
                || ((message.Flags & CommandFlags.RetryIfNotSent) != 0 && message.Status == CommandStatus.Sent)
                || message.IsAdmin
                || message.IsInternalCall
                || !(ex is RedisException)
                || !ShouldRetry(message.Status))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Tries to queue a message for retry if possible.
        /// </summary>
        /// <param name="message">The message to attempt to retry.</param>
        /// <param name="ex">The exception, what happened when this message was originally tried.</param>
        /// <returns>True if the message was queued.</returns>
        internal override bool TryQueue(Message message, Exception ex)
        {
            if (!CanRetry(message, ex))
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
        /// Gets the current length of the retry queue.
        /// </summary>
        public override int CurrentQueueLength => RetryQueue.CurrentRetryQueueLength;

        /// <summary>
        /// Determines whether to retry a command upon restoration of a lost connection.
        /// </summary>
        /// <param name="commandStatus">Status of the command.</param>
        /// <returns>True to retry the command, otherwise false.</returns>
        public override bool ShouldRetry(CommandStatus commandStatus)
            => _shouldRetry.Invoke(commandStatus);

        /// <summary>
        /// Called on heartbeat, evaluating if anything in queue has timed out and need pruning.
        /// </summary>
        public override void OnHeartbeat() => RetryQueue.CheckRetryQueueForTimeouts();

        /// <summary>
        /// Called on a multiplexer reconnect, to start sending anything in the queue.
        /// </summary>
        public override void OnReconnect() => RetryQueue.StartRetryQueueProcessor();
    }
}
