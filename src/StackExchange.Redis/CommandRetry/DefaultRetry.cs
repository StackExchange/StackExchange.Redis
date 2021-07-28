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
    public class AlwaysRetryOnConnectionException : IRetryPolicy
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="failedMessage"></param>
        /// <returns></returns>
        public bool ShouldRetryOnConnectionException(FailedCommand failedMessage)
        {
            return true;
        }
    }

    /// <summary>
    /// 
    /// </summary>
    public class RetryIfNotSentOnConnectionException : IRetryPolicy
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="failedMessage"></param>
        /// <returns></returns>
        public bool ShouldRetryOnConnectionException(FailedCommand failedMessage)
        {
            return failedMessage.Status == CommandStatus.WaitingToBeSent;
        }
    }

    /// <summary>
    /// 
    /// </summary>
    public class RetryPolicy
    {
        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public IRetryPolicy AlwaysRetryOnConnectionException()
        {
            return new AlwaysRetryOnConnectionException();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public IRetryPolicy RetryIfNotSentOnConnectionException()
        {
            return new RetryIfNotSentOnConnectionException();
        }
    }
}
