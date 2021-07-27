using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StackExchange.Redis
{
    /// <summary>
    /// Represents Failed command
    /// </summary>
    public interface IFailedCommand
    {
        /// <summary>
        /// 
        /// </summary>
        CommandStatus Status { get; }

        /// <summary>
        /// 
        /// </summary>
        public Exception Exception { get;}

        /// <summary>
        /// 
        /// </summary>
        public string Command { get; }
    }

    internal interface IInternalFailedCommand : IFailedCommand
    {
        internal bool HasTimedOut();
        Task<bool> TryResendAsync();
        void SetExceptionAndComplete(Exception ex = null);
        bool IsEndpointAvailable();
        RedisTimeoutException GetTimeoutException();
    }
}
