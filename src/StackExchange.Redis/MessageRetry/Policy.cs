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
        public static IRetryPolicy AlwaysRetry() => new GenericPolicy(failedCommand => true);

        /// <summary>
        /// 
        /// </summary>
        public static IRetryPolicy RetryIfNotYetSent() => new GenericPolicy(failedCommand => failedCommand.Status == CommandStatus.WaitingToBeSent);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="onRetry"></param>
        /// <returns></returns>
        public static IRetryPolicy AlwaysRetry(Action<FailedCommand> onRetry)
            => new GenericPolicy(failedCommand => { onRetry(failedCommand); return true; });

        /// <summary>
        /// 
        /// </summary>
        public static IRetryPolicy RetryIfNotYetSent(Action<FailedCommand> onRetry)
            => new GenericPolicy(failedCommand =>
            {
                onRetry(failedCommand);
                return failedCommand.Status == CommandStatus.WaitingToBeSent;
            });
    }
}

