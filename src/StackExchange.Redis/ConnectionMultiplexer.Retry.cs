using System;

namespace StackExchange.Redis
{
    public partial class ConnectionMultiplexer
    {
        internal CommandRetryPolicy CommandRetryPolicy { get; }

        bool IInternalConnectionMultiplexer.RetryQueueIfEligible(Message message, CommandFailureReason reason, Exception exception)
            => RetryQueueIfEligible(message, reason, exception);

        /// <summary>
        /// Tries too queue a command for retry, if it's eligible and the policy says yes.
        /// Only called internally from the library, so that base checks cannot be bypassed.
        /// </summary>
        /// <param name="message">The message that failed.</param>
        /// <param name="reason">The failure reason, e.g. no connection available.</param>
        /// <param name="exception">The exception throw on the first attempt.</param>
        /// <returns>True if the command was queued, false otherwise.</returns>
        internal bool RetryQueueIfEligible(Message message, CommandFailureReason reason, Exception exception)
        {
            // If we pass the base sanity checks, *then* allocate a FailedCommand for final checks.
            var policy = CommandRetryPolicy;
            return policy != null
                   && CommandRetryPolicy.IsEligible(message)
                   && CommandRetryPolicy.IsEligible(exception)
                   && policy.TryQueue(new FailedCommand(message, reason, exception));
        }
    }
}
