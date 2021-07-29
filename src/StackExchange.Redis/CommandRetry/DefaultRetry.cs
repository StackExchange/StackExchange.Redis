using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StackExchange.Redis
{
    /// <summary>
    /// Command Policy to have all commands being retried for connection exception
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
    /// Command Policy to have only commands that are not yet sent being retried for connection exception
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
    /// Command Policy to choose which commands will be retried on a connection exception
    /// </summary>
    public class CommandRetryPolicy
    {
        /// <summary>
        /// Command Policy to have all commands being retried for connection exception
        /// </summary>
        /// <returns></returns>
        public ICommandRetryPolicy AlwaysRetryOnConnectionException()
        {
            return new AlwaysRetryOnConnectionException();
        }

        /// <summary>
        /// Command Policy to have only commands that are not yet sent being retried for connection exception
        /// </summary>
        /// <returns></returns>
        public ICommandRetryPolicy RetryIfNotSentOnConnectionException()
        {
            return new RetryIfNotSentOnConnectionException();
        }
    }
}
