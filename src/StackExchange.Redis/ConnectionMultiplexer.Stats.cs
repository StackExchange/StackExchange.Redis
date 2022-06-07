using System;
using System.Threading;

namespace StackExchange.Redis
{
    public sealed partial class ConnectionMultiplexer
    {
        internal static long TotalCreatedMultiplexers = 0;
        internal DateTime CreationDate { get; }

        /// <summary>
        /// Tracks overall connection multiplexer counts.
        /// </summary>
        internal int _connectAttemptCount = 0,
                     _connectCompletedCount = 0,
                     _connectionCloseCount = 0;

        private long syncTimeouts,
                     fireAndForgets,
                     asyncTimeouts;

        private int _activeHeartbeatErrors,
                    lastHeartbeatTicks;

        private IDisposable? pulse;

        internal long LastHeartbeatSecondsAgo =>
            pulse is null
            ? -1
            : unchecked(Environment.TickCount - Thread.VolatileRead(ref lastHeartbeatTicks)) / 1000;

        private static int lastGlobalHeartbeatTicks = Environment.TickCount;
        internal static long LastGlobalHeartbeatSecondsAgo =>
            unchecked(Environment.TickCount - Thread.VolatileRead(ref lastGlobalHeartbeatTicks)) / 1000;
    }
}
