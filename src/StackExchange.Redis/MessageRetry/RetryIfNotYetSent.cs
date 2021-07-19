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
        /// <summary>
        /// 
        /// </summary>
        /// <param name="failedMessage"></param>
        /// <returns></returns>
        public bool ShouldRetry(FailedMessage failedMessage) => failedMessage.Status == CommandStatus.WaitingToBeSent;
    }
}
