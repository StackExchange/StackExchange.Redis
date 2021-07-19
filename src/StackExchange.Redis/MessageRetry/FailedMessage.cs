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
    public class FailedMessage
    {
        /// <summary>
        /// status of the message
        /// </summary>
        public CommandStatus Status { get; }

        /// <summary>
        /// Command being executed. For example GET, INCR etc.
        /// </summary>
        public string Command { get; }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="status"></param>
        /// <param name="command"></param>
        internal FailedMessage(CommandStatus status, RedisCommand command)
        {
            this.Status = status;
            this.Command = command.ToString();
        }
    }
}
