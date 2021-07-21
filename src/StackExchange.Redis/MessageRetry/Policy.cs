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
    public static class Policy
    {
        /// <summary>
        /// 
        /// </summary>
        public static IRetryPolicy AlwaysRetry => new GenericPolicy(failedCommand => true);

        /// <summary>
        /// 
        /// </summary>
        public static IRetryPolicy RetryIfNotYetSent => new GenericPolicy(failedCommand => failedCommand.Status == CommandStatus.WaitingToBeSent);
    }
}

