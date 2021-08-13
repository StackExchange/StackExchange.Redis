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

        internal bool IsMessageRetriable(Message message , Exception ex)
        {
            if ((message.Flags & CommandFlags.NoRetry) != 0
                    || ((message.Flags & CommandFlags.RetryIfNotSent) != 0 && message.Status == CommandStatus.Sent))
                return false;

            if (message.IsAdmin || message.IsInternalCall || !(ex is RedisException) || !ShouldRetry(message.Status))
                return false;

            return true;
        }

        bool IRetryOnReconnectPolicy.TryMessageForRetry(Message message, Exception ex)
        {
            if (!IsMessageRetriable(message, ex))
            {
                return false;
            }

            if (((IRetryOnReconnectPolicy)this).RetryQueueManager.TryHandleFailedCommand(message))
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

        void IRetryOnReconnectPolicy.Init(ConnectionMultiplexer mux)
        {
            var messageRetryHelper = new MessageRetryHelper(mux);
            ((IRetryOnReconnectPolicy)this).RetryQueueManager = new MessageRetryQueue(messageRetryHelper);
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
        /// returns the current length of the retry queue
        /// </summary>
        public int RetryQueueCurrentLength => ((IRetryOnReconnectPolicy)this).RetryQueueManager.CurrentRetryQueueLength;

        MessageRetryQueue IRetryOnReconnectPolicy.RetryQueueManager { get; set; }


        /// <summary>
        /// Determines whether to retry a command upon restoration of a lost connection
        /// </summary>
        /// <param name="commandStatus">Status of the command</param>
        /// <returns>True to retry the command, otherwise false</returns>
        public bool ShouldRetry(CommandStatus commandStatus)
            => _shouldRetry.Invoke(commandStatus);
    }
}
