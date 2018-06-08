using System;
using System.Net;
using System.Text;

namespace StackExchange.Redis
{
    /// <summary>
    /// Describes internal errors (mainly intended for debugging)
    /// </summary>
    public class InternalErrorEventArgs : EventArgs, ICompletable
    {
        private readonly EventHandler<InternalErrorEventArgs> handler;
        private readonly object sender;
        internal InternalErrorEventArgs(EventHandler<InternalErrorEventArgs> handler, object sender, EndPoint endpoint, ConnectionType connectionType, Exception exception, string origin)
        {
            this.handler = handler;
            this.sender = sender;
            EndPoint = endpoint;
            ConnectionType = connectionType;
            Exception = exception;
            Origin = origin;
        }

        /// <summary>
        /// Gets the connection-type of the failing connection
        /// </summary>
        public ConnectionType ConnectionType { get; }

        /// <summary>
        /// Gets the failing server-endpoint (this can be null)
        /// </summary>
        public EndPoint EndPoint { get; }

        /// <summary>
        /// Gets the exception if available (this can be null)
        /// </summary>
        public Exception Exception { get; }

        /// <summary>
        /// The underlying origin of the error
        /// </summary>
        public string Origin { get; }

        void ICompletable.AppendStormLog(StringBuilder sb)
        {
            sb.Append("event, internal-error: ").Append(Origin);
            if (EndPoint != null) sb.Append(", ").Append(Format.ToString(EndPoint));
        }

        bool ICompletable.TryComplete(bool isAsync)
        {
            return ConnectionMultiplexer.TryCompleteHandler(handler, sender, this, isAsync);
        }
    }
}