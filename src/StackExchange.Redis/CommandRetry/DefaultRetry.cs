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
    public class AlwaysRetryOnConnectionException : ICommandRetryPolicy
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="commandStatus"></param>
        /// <returns></returns>
        public bool ShouldRetryOnConnectionException(CommandStatus commandStatus) => true;
    }

    /// <summary>
    /// 
    /// </summary>
    public class RetryIfNotSentOnConnectionException : ICommandRetryPolicy
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="commandStatus"></param>
        /// <returns></returns>
        public bool ShouldRetryOnConnectionException(CommandStatus commandStatus)
        {
            return commandStatus == CommandStatus.WaitingToBeSent;
        }
    }

    /// <summary>
    /// 
    /// </summary>
    public class CommandRetryPolicy
    {
        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public ICommandRetryPolicy AlwaysRetryOnConnectionException()
        {
            return new AlwaysRetryOnConnectionException();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public ICommandRetryPolicy RetryIfNotSentOnConnectionException()
        {
            return new RetryIfNotSentOnConnectionException();
        }
    }
}
