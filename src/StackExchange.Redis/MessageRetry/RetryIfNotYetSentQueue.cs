using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StackExchange.Redis
{

    /// <summary>
    /// 
    /// </summary>
    public class RetryIfNotYetSentQueue : IRetryPolicy
    {
        private readonly IRetryManager MessageRetryManager;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="maxQueueLength"></param>
        public RetryIfNotYetSentQueue(int? maxQueueLength)
        {
            MessageRetryManager = new MessageRetryManager(maxQueueLength);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="failedMessage"></param>
        /// <returns></returns>
        public bool TryHandleFailedMessage(FailedMessage failedMessage)
        {
            if(failedMessage.Status == CommandStatus.WaitingToBeSent)
            {
                return MessageRetryManager.RetryMessage(failedMessage);
            }
            return false;
        }
    }
}
