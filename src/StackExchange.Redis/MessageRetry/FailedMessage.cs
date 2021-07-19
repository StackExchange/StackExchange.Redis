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
        internal ConnectionMultiplexer Multiplexer { get; }

        /// <summary>
        /// 
        /// </summary>
        public bool ResultBoxIsAsync { get; internal set; }

        /// <summary>
        /// 
        /// </summary>
        public object AsyncTimeoutMilliseconds { get; internal set; }

        /// <summary>
        /// 
        /// </summary>
        public object TimeoutMilliseconds { get; internal set; }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="message"></param>
        /// <param name="multiplexer"></param>
        internal FailedMessage(Message message, ConnectionMultiplexer multiplexer)
        {
            Message = message;
            Multiplexer = multiplexer;
        }

        internal int GetWriteTime() => Message.GetWriteTime();
        internal void ResetStatusToWaitingToBeSent() => Message.ResetStatusToWaitingToBeSent();
        internal bool IsEndpointAvailable() => Multiplexer.SelectServer(Message) != null;
        internal void SetExceptionAndComplete(RedisConnectionException inner, object p, bool onConnectionRestoreRetry) => throw new NotImplementedException();
        internal async Task TryResendAsync()
        {
            var server = Multiplexer.SelectServer(Message);
            if (server != null)
            {
                var result = await server.TryWriteAsync(Message).ForAwait();

                if (result != WriteResult.Success)
                {
                    var ex = Multiplexer.GetException(result, Message, server);
                    HandleException(Message, ex);
                }
            }
        }

        internal void HandleException(Message message, Exception ex)
        {
            var inner = new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Failed while retrying on connection restore", ex);
            message.SetExceptionAndComplete(inner, null, onConnectionRestoreRetry: false);
        }
    }

}
