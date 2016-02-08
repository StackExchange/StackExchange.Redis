﻿using System;
using System.Net;
using System.Text;

namespace StackExchange.Redis
{
    /// <summary>
    /// Contains information about a server connection failure
    /// </summary>
    public sealed class ConnectionFailedEventArgs : EventArgs, ICompletable
    {
        private readonly EventHandler<ConnectionFailedEventArgs> handler;
        private readonly object sender;
        internal ConnectionFailedEventArgs(EventHandler<ConnectionFailedEventArgs> handler, object sender, EndPoint endPoint, ConnectionType connectionType, ConnectionFailureType failureType, Exception exception)
        {
            this.handler = handler;
            this.sender = sender;
            EndPoint = endPoint;
            ConnectionType = connectionType;
            Exception = exception;
            FailureType = failureType;
        }

        /// <summary>
        /// Gets the connection-type of the failing connection
        /// </summary>
        public ConnectionType ConnectionType { get; }

        /// <summary>
        /// Gets the failing server-endpoint
        /// </summary>
        public EndPoint EndPoint { get; }

        /// <summary>
        /// Gets the exception if available (this can be null)
        /// </summary>
        public Exception Exception { get; }

        /// <summary>
        /// The type of failure
        /// </summary>
        public ConnectionFailureType FailureType { get; }

        void ICompletable.AppendStormLog(StringBuilder sb)
        {
            sb.Append("event, connection-failed: ");
            if (EndPoint == null) sb.Append("n/a");
            else sb.Append(Format.ToString(EndPoint));
        }

        bool ICompletable.TryComplete(bool isAsync)
        {
            return ConnectionMultiplexer.TryCompleteHandler(handler, sender, this, isAsync);
        }
    }
}
