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
    public class FailedCommand
    {
        /// <summary>
        /// status of the message
        /// </summary>
        public CommandStatus Status => Message.Status;

        /// <summary>
        /// 
        /// </summary>
        internal bool HasTimedOut => HasTimedOutInternal(Environment.TickCount,
                        Message.ResultBoxIsAsync ? Multiplexer.AsyncTimeoutMilliseconds : Multiplexer.TimeoutMilliseconds,
                        Message.GetWriteTime());
        /// <summary>
        /// 
        /// </summary>
        /// <param name="now"></param>
        /// <param name="timeoutMilliseconds"></param>
        /// <param name="writeTickCount"></param>
        /// <returns></returns>
        private bool HasTimedOutInternal(int now, int timeoutMilliseconds, int writeTickCount)
        {
            int millisecondsTaken = unchecked(now - writeTickCount);
            return millisecondsTaken >= timeoutMilliseconds;
        }

        /// <summary>
        /// Command being executed. For example GET, INCR etc.
        /// </summary>
        public string Command => Message.Command.ToString();

        internal Message Message { get; }
        internal ConnectionMultiplexer Multiplexer { get; }

        // I am not using ExceptionFactory.Timeout as it can cause deadlock while trying to lock writtenawaiting response queue for GetHeadMessages
        internal RedisTimeoutException GetTimeoutException()
        {
            var sb = new StringBuilder();
            sb.Append("Timeout while waiting for connectionrestore ").Append(Command).Append(" (").Append(Format.ToString(Multiplexer.TimeoutMilliseconds)).Append("ms)");
            var ex = new RedisTimeoutException(sb.ToString(), Status);
            return ex;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="message"></param>
        /// <param name="multiplexer"></param>
        internal FailedCommand(Message message, ConnectionMultiplexer multiplexer)
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
