using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StackExchange.Redis
{
    internal class MessageRetryHelper : IMessageRetryHelper
    {
        readonly IInternalConnectionMultiplexer multiplexer;

        public MessageRetryHelper(IInternalConnectionMultiplexer multiplexer)
        {
            this.multiplexer = multiplexer;
        }

        public bool HasTimedOut(Message message)
        {
            var timeoutMilliseconds = message.ResultBoxIsAsync ? multiplexer.AsyncTimeoutMilliseconds : multiplexer.TimeoutMilliseconds;
            int millisecondsTaken = unchecked(Environment.TickCount - message.GetWriteTime());
            return millisecondsTaken >= timeoutMilliseconds;
        }

        // I am not using ExceptionFactory.Timeout as it can cause deadlock while trying to lock writtenawaiting response queue for GetHeadMessages
        public RedisTimeoutException GetTimeoutException(Message message)
        {
            var sb = new StringBuilder();
            sb.Append("Timeout while waiting for connectionrestore ").Append(message.Command).Append(" (").Append(Format.ToString(multiplexer.TimeoutMilliseconds)).Append("ms)");
            var ex = new RedisTimeoutException(sb.ToString(), message.Status);
            return ex;
        }

        public bool IsEndpointAvailable(Message message) => multiplexer.SelectServer(message) != null;

        public async Task<bool> TryResendAsync(Message message)
        {
            var server = multiplexer.SelectServer(message);
            if (server != null)
            {
                var result = await server.TryWriteAsync(message).ForAwait();

                if (result != WriteResult.Success)
                {
                    var ex = multiplexer.GetException(result, message, server);
                    SetExceptionAndComplete(message, ex);
                }
                return true;
            }
            return false;
        }

        public void SetExceptionAndComplete(Message message, Exception ex = null)
        {
            var inner = new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Failed while retrying on connection restore", ex);
            message.SetExceptionAndComplete(inner, null, onConnectionRestoreRetry: false);
        }

    }
}
