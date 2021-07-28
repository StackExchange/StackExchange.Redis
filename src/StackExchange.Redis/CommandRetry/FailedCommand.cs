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
    public sealed class FailedCommand : IInternalFailedCommand
    {
        /// <summary>
        /// status of the message
        /// </summary>
        public CommandStatus Status => Message.Status;

        /// <summary>
        /// 
        /// </summary>
        internal Exception Exception {get; internal set;}
       
        
        bool IInternalFailedCommand.HasTimedOut()
        {
            var timeoutMilliseconds = Message.ResultBoxIsAsync ? Multiplexer.AsyncTimeoutMilliseconds : Multiplexer.TimeoutMilliseconds;
            int millisecondsTaken = unchecked(Environment.TickCount - Message.GetWriteTime());
            return millisecondsTaken >= timeoutMilliseconds;
        }

        /// <summary>
        /// Command being executed. For example GET, INCR etc.
        /// </summary>
        internal string Command => Message.Command.ToString();

        private Message Message { get; }
        private IInternalConnectionMultiplexer Multiplexer { get; }

        // I am not using ExceptionFactory.Timeout as it can cause deadlock while trying to lock writtenawaiting response queue for GetHeadMessages
        RedisTimeoutException IInternalFailedCommand.GetTimeoutException()
        {
            var sb = new StringBuilder();
            sb.Append("Timeout while waiting for connectionrestore ").Append(Command).Append(" (").Append(Format.ToString(Multiplexer.TimeoutMilliseconds)).Append("ms)");
            var ex = new RedisTimeoutException(sb.ToString(), Status);
            return ex;
        }

        
        internal FailedCommand(Message message, IInternalConnectionMultiplexer multiplexer, Exception ex)
        {
            Message = message;
            Multiplexer = multiplexer;
            Exception = ex;
        }

        internal int GetWriteTime() => Message.GetWriteTime();
        internal void ResetStatusToWaitingToBeSent() => Message.ResetStatusToWaitingToBeSent();
        bool IInternalFailedCommand.IsEndpointAvailable() => Multiplexer.SelectServer(Message) != null;

        async Task<bool> IInternalFailedCommand.TryResendAsync()
        {
            var server = Multiplexer.SelectServer(Message);
            if (server != null)
            {
                var result = await server.TryWriteAsync(Message).ForAwait();

                if (result != WriteResult.Success)
                {
                    var ex = Multiplexer.GetException(result, Message, server);
                    ((IInternalFailedCommand)this).SetExceptionAndComplete(ex);
                }
                return true;
            }
            return false;
        }

        void IInternalFailedCommand.SetExceptionAndComplete(Exception ex)
        {
            var inner = new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Failed while retrying on connection restore", ex);
            Message.SetExceptionAndComplete(inner, null, onConnectionRestoreRetry: false);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public override string ToString() => $"{Command} failed with {Exception}";
        
    }

}
