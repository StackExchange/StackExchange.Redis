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
        Func<FailedCommand, bool> handleResult;
        /// <summary>
        /// 
        /// </summary>
        public GenericPolicy()
        {
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

        internal IRetryPolicy Set(Func<FailedCommand, bool> p, Func<Exception, bool> handler)
        {
            this.func = p;
            this.exceptionHandler = handler;
            return this;
        }

        internal IRetryPolicy Set(Func<FailedCommand, bool> handleResult)
        {
            this.handleResult = handleResult;
            return this;
        }
    }
}
