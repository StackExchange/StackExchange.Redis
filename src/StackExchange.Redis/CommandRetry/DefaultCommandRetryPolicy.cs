using System;

namespace StackExchange.Redis
{
    /// <summary>
    /// Command retry policy to determine which commands will be retried after a lost connection is retored
    /// </summary>
    public class DefaultCommandRetryPolicy : CommandRetryPolicy
    {
        private MessageRetryQueue RetryQueue { get; }

        private readonly Func<FailedCommand, bool> _shouldRetry;

        /// <summary>
        /// Creates a <see cref="DefaultCommandRetryPolicy"/> for the given <see cref="ConnectionMultiplexer"/>.
        /// </summary>
        /// <param name="muxer">The <see cref="ConnectionMultiplexer"/> to handle retries for.</param>
        /// <param name="shouldRetry">Whether a command should be retried.</param>
        protected internal DefaultCommandRetryPolicy(ConnectionMultiplexer muxer, Func<FailedCommand, bool> shouldRetry) : base(muxer)
        {
            _shouldRetry = shouldRetry;
            var messageRetryHelper = new MessageRetryHelper(muxer);
            RetryQueue = new MessageRetryQueue(messageRetryHelper);
        }

        /// <summary>
        /// Gets the current length of the retry queue.
        /// </summary>
        public override int CurrentQueueLength => RetryQueue.CurrentRetryQueueLength;

        /// <summary>
        /// Returns whether the current queue is processing (e.g. retrying queued commands).
        /// </summary>
        public override bool CurrentlyProcessing => RetryQueue.IsRunning;

        /// <summary>
        /// Returns whether the current queue is processing (e.g. retrying queued commands).
        /// </summary>
        public override string StatusDescription => RetryQueue.StatusDescription;

        /// <summary>
        /// Tries to queue a message for retry if possible.
        /// </summary>
        /// <param name="command">The command to tru queueing (contains the message and exception).</param>
        /// <returns>True if the message was queued.</returns>
        /// <remarks>Note that this is internal only - external callers cannot override it to bypass the CanRetry checks.</remarks>
        protected internal override bool TryQueue(FailedCommand command)
        {
            // Sanity check if we should be trying this one
            if (!_shouldRetry.Invoke(command))
            {
                return false;
            }

            if (RetryQueue.TryHandleFailedCommand(command.Message))
            {
                // if this message is a new message set the writetime
                if (command.Message.GetWriteTime() == 0)
                {
                    command.Message.SetEnqueued(null);
                }

                command.Message.ResetStatusToWaitingToBeSent();

                return true;
            }

            return false;
        }

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
