using System;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace StackExchange.Redis
{
    internal sealed class ThreadSafePhysicalConnectionAccessor
    {
        private PhysicalConnection? physical;
        private readonly ReaderWriterLockSlim rwLock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
        internal ThreadSafePhysicalConnectionAccessor()
        {
        }

        internal bool HasOutputPipe
        {
            get
            {
                try
                {
                    rwLock.EnterReadLock();
                    return physical?.HasOutputPipe ?? false;
                }
                finally
                {
                    rwLock.ExitReadLock();
                }
            }
        }

        internal bool HasPendingCallerFacingItems
        {
            get
            {
                try
                {
                    rwLock.EnterReadLock();
                    return physical?.HasPendingCallerFacingItems() ?? false;
                }
                finally
                {
                    rwLock.ExitReadLock();
                }
            }
        }

        internal void CreateConnection(PhysicalBridge bridge, ILogger? log)
        {
            try
            {
                rwLock.EnterWriteLock();
                physical = new PhysicalConnection(bridge);
                physical.BeginConnectAsync(log).RedisFireAndForget();
            }
            finally
            {
                rwLock.ExitWriteLock();
            }
        }

        internal long? GetConnectionId()
        {
            try
            {
                rwLock.EnterReadLock();
                return physical?.ConnectionId;
            }
            finally
            {
                rwLock.ExitReadLock();
            }
        }

        internal string? GetPhysicalName()
        {
            try
            {
                rwLock.EnterReadLock();
                return physical?.ToString();
            }
            finally
            {
                rwLock.ExitReadLock();
            }
        }

        internal long GetSubscriptionCount()
        {
            try
            {
                rwLock.EnterReadLock();
                return physical?.SubscriptionCount ?? 0;
            }
            finally
            {
                rwLock.ExitReadLock();
            }
        }

        internal void DisposePhysicalConnection()
        {
            try
            {
                rwLock.EnterWriteLock();
                if (physical != null)
                {
                    physical.Dispose();
                    physical = null;
                }
            }
            finally
            {
                rwLock.ExitWriteLock();
            }
        }

        internal void Shutdown()
        {
            try
            {
                rwLock.EnterWriteLock();
                try
                {
                    physical?.Shutdown();
                }
                catch { }
            }
            finally
            {
                rwLock.ExitWriteLock();
            }
        }

        internal bool WorkOnPhysicalWithLock(Action<PhysicalConnection> callBack)
        {
            try
            {
                rwLock.EnterWriteLock();
                if (physical != null)
                {
                    callBack(physical);
                    return true;
                }
                return false;
            }
            finally
            {
                rwLock.ExitWriteLock();
            }
        }

        internal void SimulateConnectionFailure(SimulatedFailureType failureType)
        {
            try
            {
                rwLock.EnterWriteLock();
                physical?.SimulateConnectionFailure(failureType);
            }
            finally
            {
                rwLock.ExitWriteLock();
            }
        }

        internal void SetIdle()
        {
            try
            {
                rwLock.EnterWriteLock();
                physical?.SetIdle();
            }
            finally
            {
                rwLock.ExitWriteLock();
            }
        }

        internal void GetCounters(ConnectionCounters counters)
        {
            try
            {
                rwLock.EnterReadLock();
                physical?.GetCounters(counters);
            }
            finally
            {
                rwLock.ExitReadLock();
            }
        }

        internal PhysicalConnection.ConnectionStatus GetStatus()
        {
            try
            {
                rwLock.EnterReadLock();
                return physical?.GetStatus() ?? PhysicalConnection.ConnectionStatus.Default;
            }
            finally
            {
                rwLock.ExitReadLock();
            }
        }

        internal void GetStormLog(StringBuilder sb)
        {
            try
            {
                rwLock.EnterReadLock();
                physical?.GetStormLog(sb);
            }
            finally
            {
                rwLock.ExitReadLock();
            }
        }

        internal bool IsIdle()
        {
            try
            {
                rwLock.EnterReadLock();
                return physical?.IsIdle() ?? false;
            }
            finally
            {
                rwLock.ExitReadLock();
            }
        }

        internal bool ConnectionMatch(PhysicalConnection connection)
        {
            try
            {
                rwLock.EnterReadLock();
                return physical == connection;
            }
            finally
            {
                rwLock.ExitReadLock();
            }
        }

        internal bool ClearPhysicalIfMatch(PhysicalConnection? connection, out bool isNull)
        {
            try
            {
                rwLock.EnterWriteLock();
                isNull = physical == null;
                if (physical == connection)
                {
                    physical = null;
                    return true;
                }
                return false;
            }
            finally
            {
                rwLock.ExitWriteLock();
            }
        }
    }
}
