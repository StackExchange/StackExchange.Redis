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
        /// The endpoint involved in this event (this can be null)
        /// </summary>
        public EndPoint EndPoint { get; }

        void ICompletable.AppendStormLog(StringBuilder sb)
        {
            sb.Append("event, endpoint: ");
            if (EndPoint == null) sb.Append("n/a");
            else sb.Append(Format.ToString(EndPoint));
        }

        bool ICompletable.TryComplete(bool isAsync)
        {
            return ConnectionMultiplexer.TryCompleteHandler(handler, sender, this, isAsync);
        }
    }
}