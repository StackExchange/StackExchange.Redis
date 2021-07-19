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
    public class AlwaysRetryQueue : IRetryPolicy
    {
        private readonly IRetryManager MessageRetryManager;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="maxQueueLength"></param>
        public AlwaysRetryQueue(int? maxQueueLength = null)
        {
            MessageRetryManager = new MessageRetryManager(maxQueueLength);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="failedMessage"></param>
        /// <returns></returns>
        public bool TryHandleFailedMessage(FailedMessage failedMessage) => MessageRetryManager.RetryMessage(failedMessage);
    }
}
