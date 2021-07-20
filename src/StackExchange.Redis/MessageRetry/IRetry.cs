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
        /// 
        /// </summary>
        /// <param name="failedMessage"></param>
        /// <returns></returns>
        public bool TryHandleFailedMessage(FailedCommand failedMessage);
    }
}
