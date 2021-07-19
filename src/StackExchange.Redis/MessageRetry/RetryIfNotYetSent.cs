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
    public class RetryIfNotYetSent : IRetryPolicy
    {
        private readonly IRetryManager MessageRetryManager;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="maxQueueLength"></param>
        public RetryIfNotYetSent(int? maxQueueLength = null)
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
