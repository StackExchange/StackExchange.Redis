using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace StackExchange.Redis
{
    /// <summary>
    /// 
    /// </summary>
    public class RetryNTimes : IRetryPolicy
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="failedMessage"></param>
        /// <returns></returns>
        public bool TryHandleFailedMessage(FailedMessage failedMessage)
        {
            return failedMessage.TryResendAsync().Wait();
        }
    }
}
