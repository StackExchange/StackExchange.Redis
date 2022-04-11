#nullable enable

using System;
using System.Diagnostics;

namespace StackExchange.Redis.Transports
{
    internal interface ITransportState
    {
        CommandMap CommandMap { get; }
        byte[]? ChannelPrefix { get; }
        ServerEndPoint? ServerEndPoint { get; }

        [Obsolete("Please use " + nameof(ITransportStateExtensions.OnTransactionLog) + " instead")]
        void OnTransactionLogImpl(string message);
    }

    internal static class ITransportStateExtensions
    {
        [Conditional("VERBOSE")]
        internal static void OnTransactionLog(this ITransportState? state, string message)
        {
#if VERBOSE
#pragma warning disable CS0618
            state?.OnTransactionLogImpl(message);
#pragma warning restore CS0618
#endif
        }
    }
}
