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
        /// <param name="ismessageAlreadySent"></param>
        /// <returns></returns>
        public bool ShouldRetry(bool ismessageAlreadySent) => !ismessageAlreadySent;
    }
}
