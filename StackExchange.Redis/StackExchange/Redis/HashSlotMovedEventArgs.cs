using System;
using System.Net;
using System.Text;

namespace StackExchange.Redis
{
    /// <summary>
    /// Contains information about individual hash-slot relocations
    /// </summary>
    public sealed class HashSlotMovedEventArgs : EventArgs, ICompletable
    {
        private readonly object sender;
        private readonly EventHandler<HashSlotMovedEventArgs> handler;
        /// <summary>
        /// The hash-slot that was relocated
        /// </summary>
        public int HashSlot { get; }

        /// <summary>
        /// The old endpoint for this hash-slot (if known)
        /// </summary>
        public EndPoint OldEndPoint { get; }

        /// <summary>
        /// The new endpoint for this hash-slot (if known)
        /// </summary>
        public EndPoint NewEndPoint { get; }

        internal HashSlotMovedEventArgs(EventHandler<HashSlotMovedEventArgs> handler, object sender,
            int hashSlot, EndPoint old, EndPoint @new)
        {
            this.handler = handler;
            this.sender = sender;
            HashSlot = hashSlot;
            OldEndPoint = old;
            NewEndPoint = @new;
        }

        bool ICompletable.TryComplete(bool isAsync)
        {
            return ConnectionMultiplexer.TryCompleteHandler(handler, sender, this, isAsync);
        }

        void ICompletable.AppendStormLog(StringBuilder sb)
        {
            sb.Append("event, slot-moved: ").Append(HashSlot);
        }
    }
}
