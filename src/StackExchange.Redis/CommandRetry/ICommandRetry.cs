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
    public interface ICommandRetryPolicy
    {
        /// <summary>
        /// Called when a message failed due to connection error
        /// </summary>
        /// <param name="commandStatus">current state of the command</param>
        /// <returns></returns>
        public bool ShouldRetryOnConnectionException(CommandStatus commandStatus);
        
    }
}
