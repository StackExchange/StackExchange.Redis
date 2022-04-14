#nullable enable

using System;
using System.Diagnostics;

namespace StackExchange.Redis.Transports
{
    internal interface IBridge
    {
        WriteResult Write(Message message);
    }

    internal interface ITransportState
    {
        CommandMap CommandMap { get; }
        byte[]? ChannelPrefix { get; }
        ServerEndPoint? ServerEndPoint { get; }
        ConnectionType ConnectionType { get; }
        [Obsolete("Please use " + nameof(TransportStateExtensions.OnTransactionLog) + " instead")]
        void OnTransactionLogImpl(string message);

        [Obsolete("Please use " + nameof(TransportStateExtensions.Trace) + " instead")]
        void TraceImpl(string message);
        void RecordConnectionFailed(ConnectionFailureType failureType, Exception? exception = null);
    }

    internal static class TransportStateExtensions
    {
        [Conditional("VERBOSE")]
        internal static void OnTransactionLog(this ITransportState state, string message)
        {
#if VERBOSE
#pragma warning disable CS0618
            state.OnTransactionLogImpl(message);
#pragma warning restore CS0618
#endif
        }

        [Conditional("VERBOSE")]
        internal static void Trace(this ITransportState state, string message)
        {
#if VERBOSE
#pragma warning disable CS0618
            state.TraceImpl(message);
#pragma warning restore CS0618
#endif
        }
    }
}
