using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StackExchange.Redis
{
    /// <summary>
    /// interface to implement command retry policy
    /// </summary>
    public interface IRetryPolicy
    {
        /// <summary>
        /// Called when a message failed due to connection error
        /// </summary>
        /// <param name="failedMessage"></param>
        /// <returns></returns>
        public bool ShouldRetry(FailedCommand failedMessage);
    }
}
