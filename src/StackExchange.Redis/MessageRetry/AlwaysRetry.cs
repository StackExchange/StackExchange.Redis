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
        /// <summary>
        /// 
        /// </summary>
        /// <param name="ismessageAlreadySent"></param>
        /// <returns></returns>
        public bool ShouldRetry(bool ismessageAlreadySent) => true;

    }
}
