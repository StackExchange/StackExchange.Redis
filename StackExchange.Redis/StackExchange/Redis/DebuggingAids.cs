using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace StackExchange.Redis
{
#if DEBUG

    partial class ResultBox
    {
        internal static long allocations;

        public static long GetAllocationCount()
        {
            return Interlocked.Read(ref allocations);
        }
        static partial void OnAllocated()
        {
            Interlocked.Increment(ref allocations);
        }
    }
    partial interface IServer
    {
        /// <summary>
        /// Show what is in the pending (unsent) queue
        /// </summary>
        string ListPending(int maxCount);
        /// <summary>
        /// Get the value of key. If the key does not exist the special value nil is returned. An error is returned if the value stored at key is not a string, because GET only handles string values.
        /// </summary>
        /// <returns>the value of key, or nil when key does not exist.</returns>
        /// <remarks>http://redis.io/commands/get</remarks>
        RedisValue StringGet(int db, RedisKey key, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Get the value of key. If the key does not exist the special value nil is returned. An error is returned if the value stored at key is not a string, because GET only handles string values.
        /// </summary>
        /// <returns>the value of key, or nil when key does not exist.</returns>
        /// <remarks>http://redis.io/commands/get</remarks>
        Task<RedisValue> StringGetAsync(int db, RedisKey key, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Break the connection without mercy or thought
        /// </summary>
        void SimulateConnectionFailure();

        /// <summary>
        /// DEBUG SEGFAULT performs an invalid memory access that crashes Redis. It is used to simulate bugs during the development.
        /// </summary>
        /// <remarks>http://redis.io/commands/debug-segfault</remarks>
        void Crash();

        /// <summary>
        /// CLIENT PAUSE is a connections control command able to suspend all the Redis clients for the specified amount of time (in milliseconds).
        /// </summary>
        /// <remarks>http://redis.io/commands/client-pause</remarks>
        void Hang(TimeSpan duration, CommandFlags flags = CommandFlags.None);
    }
    partial interface IRedis
    {
        /// <summary>
        /// The CLIENT GETNAME returns the name of the current connection as set by CLIENT SETNAME. Since every new connection starts without an associated name, if no name was assigned a null string is returned.
        /// </summary>
        /// <remarks>http://redis.io/commands/client-getname</remarks>
        /// <returns>The connection name, or a null string if no name is set.</returns>
        string ClientGetName(CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Ask the server to close the connection. The connection is closed as soon as all pending replies have been written to the client.
        /// </summary>
        /// <remarks>http://redis.io/commands/quit</remarks>
        void Quit(CommandFlags flags = CommandFlags.None);
    }

    partial interface IRedisAsync
    {
        /// <summary>
        /// The CLIENT GETNAME returns the name of the current connection as set by CLIENT SETNAME. Since every new connection starts without an associated name, if no name was assigned a null string is returned.
        /// </summary>
        /// <remarks>http://redis.io/commands/client-getname</remarks>
        /// <returns>The connection name, or a null string if no name is set.</returns>
        Task<string> ClientGetNameAsync(CommandFlags flags = CommandFlags.None);
    }
    partial class RedisBase
    {
        string IRedis.ClientGetName(CommandFlags flags)
        {
            var msg = Message.Create(-1, flags, RedisCommand.CLIENT, RedisLiterals.GETNAME);
            return ExecuteSync(msg, ResultProcessor.String);
        }

        Task<string> IRedisAsync.ClientGetNameAsync(CommandFlags flags)
        {
            var msg = Message.Create(-1, flags, RedisCommand.CLIENT, RedisLiterals.GETNAME);
            return ExecuteAsync(msg, ResultProcessor.String);
        }
    }

    partial class ServerEndPoint
    {

        internal void SimulateConnectionFailure()
        {
            var tmp = interactive;
            tmp?.SimulateConnectionFailure();
            tmp = subscription;
            tmp?.SimulateConnectionFailure();
        }
        internal string ListPending(int maxCount)
        {
            var sb = new StringBuilder();
            var tmp = interactive;
            tmp?.ListPending(sb, maxCount);
            tmp = subscription;
            tmp?.ListPending(sb, maxCount);
            return sb.ToString();
        }
    }

    partial class RedisServer
    {
        void IServer.SimulateConnectionFailure()
        {
            server.SimulateConnectionFailure();
        }
        string IServer.ListPending(int maxCount)
        {
            return server.ListPending(maxCount);
        }
        void IServer.Crash()
        {
            // using DB-0 because we also use "DEBUG OBJECT", which is db-centric
            var msg = Message.Create(0, CommandFlags.FireAndForget, RedisCommand.DEBUG, RedisLiterals.SEGFAULT);
            ExecuteSync(msg, ResultProcessor.DemandOK);
        }
        void IServer.Hang(TimeSpan duration, CommandFlags flags)
        {
            var msg = Message.Create(-1, flags, RedisCommand.CLIENT, RedisLiterals.PAUSE, (long)duration.TotalMilliseconds);
            ExecuteSync(msg, ResultProcessor.DemandOK);
        }
    }

    partial class CompletionManager
    {
        private static long asyncCompletionWorkerCount;

        partial void OnCompletedAsync()
        {
            Interlocked.Increment(ref asyncCompletionWorkerCount);
        }
        internal static long GetAsyncCompletionWorkerCount()
        {
            return Interlocked.Read(ref asyncCompletionWorkerCount);
        }
    }

    partial class ConnectionMultiplexer
    {
        /// <summary>
        /// Gets how many result-box instances were allocated
        /// </summary>
        public static long GetResultBoxAllocationCount()
        {
            return ResultBox.GetAllocationCount();
        }
        /// <summary>
        /// Gets how many async completion workers were queueud
        /// </summary>
        public static long GetAsyncCompletionWorkerCount()
        {
            return CompletionManager.GetAsyncCompletionWorkerCount();
        }
        /// <summary>
        /// For debugging; when not enabled, servers cannot connect
        /// </summary>
        public bool AllowConnect { get { return allowConnect; } set { allowConnect = value; } }
        private volatile bool allowConnect = true, ignoreConnect = false;

        /// <summary>
        /// For debugging; when not enabled, end-connect is silently ignored (to simulate a long-running connect)
        /// </summary>
        public bool IgnoreConnect { get { return ignoreConnect; } set { ignoreConnect = value; } }
    }

    partial class SocketManager
    {
        partial void ShouldIgnoreConnect(ISocketCallback callback, ref bool ignore)
        {
            ignore = callback.IgnoreConnect;
        }

        /// <summary>
        /// Completion type for BeginConnect call
        /// </summary>
        public static CompletionType ConnectCompletionType { get; set; }

        partial void ShouldForceConnectCompletionType(ref CompletionType completionType)
        {
            completionType = SocketManager.ConnectCompletionType;
        }
    }
    partial interface ISocketCallback
    {
        bool IgnoreConnect { get; }
    }

    partial class MessageQueue
    {
        internal void ListPending(StringBuilder sb, int maxCount)
        {
            lock (regular)
            {
                foreach (var item in high)
                {
                    if (--maxCount < 0) break;
                    if (sb.Length != 0) sb.Append(",");
                    item.AppendStormLog(sb);
                }
                foreach (var item in regular)
                {
                    if (--maxCount < 0) break;
                    if (sb.Length != 0) sb.Append(",");
                    item.AppendStormLog(sb);
                }
            }
        }
    }

    partial class PhysicalBridge
    {
        internal void SimulateConnectionFailure()
        {
            if (!Multiplexer.RawConfig.AllowAdmin)
            {
                throw ExceptionFactory.AdminModeNotEnabled(Multiplexer.IncludeDetailInExceptions, RedisCommand.DEBUG, null, ServerEndPoint); // close enough
            }
            physical?.RecordConnectionFailed(ConnectionFailureType.SocketFailure);
        }
        internal void ListPending(StringBuilder sb, int maxCount)
        {
            queue.ListPending(sb, maxCount);
        }
    }

    partial class PhysicalConnection
    {
        partial void OnDebugAbort()
        {
            if (!Multiplexer.AllowConnect)
            {
                throw new RedisConnectionException(ConnectionFailureType.InternalFailure, "debugging");
            }
        }

        bool ISocketCallback.IgnoreConnect => Multiplexer.IgnoreConnect;

        private static volatile bool emulateStaleConnection;
        public static bool EmulateStaleConnection 
        {
            get
            {
                return emulateStaleConnection;
            }
            set
            {
                emulateStaleConnection = value;
            }
        }

        partial void DebugEmulateStaleConnection(ref int firstUnansweredWrite)
        {
            if (emulateStaleConnection)
            {
                firstUnansweredWrite = Environment.TickCount - 100000;
            }
        }
    }
#endif

    /// <summary>
    /// Completion type for CompletionTypeHelper
    /// </summary>
    public enum CompletionType
    {
        /// <summary>
        /// Retain original completion type (either sync or async)
        /// </summary>
        Any = 0,
        /// <summary>
        /// Force sync completion
        /// </summary>
        Sync = 1,
        /// <summary>
        /// Force async completion
        /// </summary>
        Async = 2
    }
#if !CORE_CLR

    internal static class PerfCounterHelper
    {
        static object staticLock = new object();
        static volatile PerformanceCounter _cpu;
        static volatile bool _disabled;

        public static bool TryGetSystemCPU(out float value)
        {
            value = -1;

            try
            {   
                if (!_disabled && _cpu == null)
                {
                    lock (staticLock)
                    {
                        if (_cpu == null)
                        {
                            _cpu = new PerformanceCounter("Processor", "% Processor Time", "_Total");

                            // First call always returns 0, so get that out of the way.
                            _cpu.NextValue();
                        }
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Some environments don't allow access to Performance Counters, so stop trying.
                _disabled = true;
            }
            catch (Exception)
            {
                // this shouldn't happen, but just being safe...
            }

            if (_cpu != null)
            {
                value = _cpu.NextValue();
                return true;
            }

            return false;
        }
    }
    
    internal class CompletionTypeHelper
    {

        public static void RunWithCompletionType(Func<AsyncCallback, IAsyncResult> beginAsync, AsyncCallback callback, CompletionType completionType)
        { 
            AsyncCallback proxyCallback;
            if (completionType == CompletionType.Any)
            {
                proxyCallback =  (ar) =>
                {
                    if (!ar.CompletedSynchronously)
                    {
                        callback(ar);
                    }
                };
            }
            else
            {
                proxyCallback = (ar) => { };
            }

            var result = beginAsync(proxyCallback);

            if (completionType == CompletionType.Any && !result.CompletedSynchronously)
            {
                return;
            }

            result.AsyncWaitHandle.WaitOne();

            switch (completionType)
            { 
                case CompletionType.Async:
                    ThreadPool.QueueUserWorkItem((s) => { callback(result); });
                    break;
                case CompletionType.Any:
                case CompletionType.Sync:
                    callback(result);
                    break;
            }

            return;
        }
    }
#endif

#if VERBOSE

    partial class ConnectionMultiplexer
    {
        private readonly int epoch = Environment.TickCount;

        partial void OnTrace(string message, string category)
        {
            Debug.WriteLine(message,
                ((Environment.TickCount - epoch)).ToString().PadLeft(5, ' ') + "ms on " +
                Environment.CurrentManagedThreadId + " ~ " + category);
        }
        static partial void OnTraceWithoutContext(string message, string category)
        {
            Debug.WriteLine(message, Environment.CurrentManagedThreadId + " ~ " + category);
        }

        partial void OnTraceLog(TextWriter log, string caller)
        {
            lock (UniqueId)
            {
                Trace(log.ToString(), caller); // note that this won't always be useful, but we only do it in debug builds anyway
            }
        }
    }
#endif


#if LOGOUTPUT
    partial class ConnectionMultiplexer
    {
        /// <summary>
        /// Dumps a copy of the stream
        /// </summary>
        public static string EchoPath { get; set; }
    }

    partial class PhysicalConnection
    {
        private Stream echo;
        partial void OnCreateEcho()
        {
            if (!string.IsNullOrEmpty(ConnectionMultiplexer.EchoPath))
            {
                string fullPath = Path.Combine(ConnectionMultiplexer.EchoPath,
                    Regex.Replace(physicalName, @"[\-\.\@\#\:]", "_"));
                echo = File.Open(Path.ChangeExtension(fullPath, "txt"), FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
            }
        }
        partial void OnCloseEcho()
        {
            if (echo != null)
            {
                try { echo.Close(); } catch { }
                try { echo.Dispose(); } catch { }
                echo = null;
            }
        }
        partial void OnWrapForLogging(ref Stream stream, string name)
        {
            stream = new LoggingTextStream(stream, physicalName, echo);
        }
    }
#endif
}
