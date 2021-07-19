using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StackExchange.Redis
{
    /// <summary>
    /// interface to implement retry manager
    /// </summary>
    public interface IRetryManager
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="failedMessage"></param>
        /// <returns></returns>
        bool RetryMessage(FailedMessage failedMessage);
    }
}
