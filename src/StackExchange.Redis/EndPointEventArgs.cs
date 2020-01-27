using System;
using System.Net;
using System.Text;

namespace StackExchange.Redis
{
    /// <summary>
    /// Event information related to redis endpoints
    /// </summary>
    public class EndPointEventArgs : EventArgs, ICompletable
    {
        private readonly EventHandler<EndPointEventArgs> handler;
        private readonly object sender;
        internal EndPointEventArgs(EventHandler<EndPointEventArgs> handler, object sender, EndPoint endpoint)
        {
            this.handler = handler;
            this.sender = sender;
            EndPoint = endpoint;
        }

        /// <summary>
        /// This constructor is only for testing purposes.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="endpoint">Redis endpoint.</param>
        public EndPointEventArgs(object sender, EndPoint endpoint)
            : this (null, sender, endpoint)
        {
        }

        /// <summary>
        /// The endpoint involved in this event (this can be null)
        /// </summary>
        public EndPoint EndPoint { get; }

        void ICompletable.AppendStormLog(StringBuilder sb)
        {
            sb.Append("event, endpoint: ");
            if (EndPoint == null) sb.Append("n/a");
            else sb.Append(Format.ToString(EndPoint));
        }

        bool ICompletable.TryComplete(bool isAsync) => ConnectionMultiplexer.TryCompleteHandler(handler, sender, this, isAsync);
    }
}
