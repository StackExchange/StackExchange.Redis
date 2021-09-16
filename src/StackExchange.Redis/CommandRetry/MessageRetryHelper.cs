using System;
using System.Text;
using System.Threading.Tasks;

namespace StackExchange.Redis
{
    internal class MessageRetryHelper : IMessageRetryHelper
    {
        private readonly IInternalConnectionMultiplexer multiplexer;

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

        /// <summary>
        /// Gets the timeout exception for a message.
        /// </summary>
        /// <param name="message">The messae to get a message for</param>
        /// <returns></returns>
        /// <remarks>
        /// Not using ExceptionFactory.Timeout as it can cause deadlock while trying to lock writtenawaiting response queue for GetHeadMessages.
        /// </remarks>
        public RedisTimeoutException GetTimeoutException(Message message)
        {
            var sb = new StringBuilder();
            sb.Append("Timeout while waiting for connectionrestore ").Append(message.Command).Append(" (").Append(Format.ToString(multiplexer.TimeoutMilliseconds)).Append("ms)");
            var ex = new RedisTimeoutException(sb.ToString(), message.Status);
            return ex;
        }

        public bool IsEndpointAvailable(Message message) => multiplexer.SelectServer(message) != null;

        /// <summary>
        /// Tries to re-issue a <see cref="Message"/>.
        /// </summary>
        /// <param name="message">The message to re-send.</param>
        /// <returns>Whether the write was successful.</returns>
        public async Task<bool> TryResendAsync(Message message)
        {
            // Use a specific server if one was specified originally, otherwise auto-select
            // This is important for things like REPLICAOF we really don't want going to another location
            var server = message.SpecificServer ?? multiplexer.SelectServer(message);
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
            var inner = new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Failed while retrying on connection restore: " + ex.Message, ex);
            message.SetExceptionAndComplete(inner, null, CommandFailureReason.RetryFailure);
        }
    }
}
