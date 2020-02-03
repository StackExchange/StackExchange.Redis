using System;
using System.Net;
using System.Text;

namespace StackExchange.Redis
{
    /// <summary>
    /// Notification of errors from the redis server
    /// </summary>
    public class RedisErrorEventArgs : EventArgs, ICompletable
    {
        private readonly EventHandler<RedisErrorEventArgs> handler;
        private readonly object sender;
        internal RedisErrorEventArgs(
            EventHandler<RedisErrorEventArgs> handler, object sender,
            EndPoint endpoint, string message)
        {
            this.handler = handler;
            this.sender = sender;
            Message = message;
            EndPoint = endpoint;
        }

        /// <summary>
        /// This constructor is only for testing purposes.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="endpoint">Redis endpoint.</param>
        /// <param name="message">Error message.</param>
        public RedisErrorEventArgs(object sender, EndPoint endpoint, string message)
            : this (null, sender, endpoint, message)
        {
        }

        /// <summary>
        /// The origin of the message
        /// </summary>
        public EndPoint EndPoint { get; }

        /// <summary>
        /// The message from the server
        /// </summary>
        public string Message { get; }

        void ICompletable.AppendStormLog(StringBuilder sb)
        {
            sb.Append("event, error: ").Append(Message);
        }

        bool ICompletable.TryComplete(bool isAsync) => ConnectionMultiplexer.TryCompleteHandler(handler, sender, this, isAsync);
    }
}
