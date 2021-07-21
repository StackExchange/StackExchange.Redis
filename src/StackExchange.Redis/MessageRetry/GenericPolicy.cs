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
    public class GenericPolicy : IRetryPolicy
    {
        Func<FailedCommand, bool> func;
        /// <summary>
        /// 
        /// </summary>
        public GenericPolicy(Func<FailedCommand, bool> func)
        {
            this.func = func;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="failedMessage"></param>
        /// <returns></returns>
        public bool ShouldRetry(FailedCommand failedMessage) => func(failedMessage);

    }
}
