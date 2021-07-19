using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StackExchange.Redis
{
    /// <summary>
    /// implements Retry policy to retry all command failed due to connection error
    /// </summary>
    public class AlwaysRetry : IRetryPolicy
    {
        private readonly MessageRetryManager MessageRetryManager;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="maxRetryQueueLength"></param>
        public AlwaysRetry(int? maxRetryQueueLength = null)
        {
            MessageRetryManager = new MessageRetryManager(maxRetryQueueLength);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="failedMessage"></param>
        /// <returns></returns>
        public bool ShouldRetry(FailedMessage failedMessage) => MessageRetryManager.RetryMessage(failedMessage);
    }


    /// <summary>
    /// implements Retry policy to retry all command failed due to connection error
    /// </summary>
    public class AlwaysRetryExceptINCR : IRetryPolicy
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="failedMessage"></param>
        /// <returns></returns>
        public bool ShouldRetry(FailedMessage failedMessage)
        {
            return !failedMessage.Command.Contains("INCR");
        }
    }
}
