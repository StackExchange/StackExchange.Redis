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
        Func<Exception, bool> exceptionHandler;
        /// <summary>
        /// 
        /// </summary>
        public GenericPolicy(Func<FailedCommand, bool> func, Func<Exception, bool> exceptionHandler)
        {
            this.func = func;
            this.exceptionHandler = exceptionHandler;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="failedMessage"></param>
        /// <returns></returns>
        public bool ShouldRetry(FailedCommand failedMessage)
        {
            if (exceptionHandler(failedMessage.Exception))
                return func(failedMessage);
            return false;
        }


    }
}
