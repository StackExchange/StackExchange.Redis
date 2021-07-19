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
        public CommandStatus Status => Message.Status;

        /// <summary>
        /// Command being executed. For example GET, INCR etc.
        /// </summary>
        public string Command => Message.Command.ToString();

        internal Message Message { get; }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="message"></param>
        /// 
        internal FailedMessage(Message message)
        {
            Message = message;
        }
    }
}
