using System;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using System.Collections.Generic;

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
            Interlocked.Increment(ref ResultBox.allocations);
        }
    }
    /// <summary>
    /// Additional IRedisServer methods for debugging
    /// </summary>
    public interface IRedisServerDebug : IServer
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
    /// <summary>
    /// Additional IRedis methods for debugging
    /// </summary>
    public interface IRedisDebug : IRedis, IRedisDebugAsync
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
    /// <summary>
    /// Additional IRedisAsync methods for debugging
    /// </summary>
    public interface IRedisDebugAsync : IRedisAsync
    {
        /// <summary>
        /// The CLIENT GETNAME returns the name of the current connection as set by CLIENT SETNAME. Since every new connection starts without an associated name, if no name was assigned a null string is returned.
        /// </summary>
        /// <remarks>http://redis.io/commands/client-getname</remarks>
        /// <returns>The connection name, or a null string if no name is set.</returns>
        Task<string> ClientGetNameAsync(CommandFlags flags = CommandFlags.None);
    }
    partial class RedisBase : IRedisDebug
    {
        string IRedisDebug.ClientGetName(CommandFlags flags)
        {
            var msg = Message.Create(-1, flags, RedisCommand.CLIENT, RedisLiterals.GETNAME);
            return ExecuteSync(msg, ResultProcessor.String);
        }

        Task<string> IRedisDebugAsync.ClientGetNameAsync(CommandFlags flags)
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
            if (tmp != null) tmp.SimulateConnectionFailure();
            tmp = subscription;
            if (tmp != null) tmp.SimulateConnectionFailure();
        }
        internal string ListPending(int maxCount)
        {
            var sb = new StringBuilder();
            var tmp = interactive;
            if (tmp != null) tmp.ListPending(sb, maxCount);
            tmp = subscription;
            if (tmp != null) tmp.ListPending(sb, maxCount);
            return sb.ToString();
        }
    }

    partial class RedisServer : IRedisServerDebug
    {
        void IRedisServerDebug.SimulateConnectionFailure()
        {
            server.SimulateConnectionFailure();
        }
        string IRedisServerDebug.ListPending(int maxCount)
        {
            return server.ListPending(maxCount);
        }
        void IRedisServerDebug.Crash()
        {
            // using DB-0 because we also use "DEBUG OBJECT", which is db-centric
            var msg = Message.Create(0, CommandFlags.FireAndForget, RedisCommand.DEBUG, RedisLiterals.SEGFAULT);
            ExecuteSync(msg, ResultProcessor.DemandOK);
        }
        void IRedisServerDebug.Hang(TimeSpan duration, CommandFlags flags)
        {
            var msg = Message.Create(0, flags, RedisCommand.CLIENT, RedisLiterals.PAUSE, (long)duration.TotalMilliseconds);
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
        private volatile bool allowConnect = true;
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
            if (!multiplexer.RawConfig.AllowAdmin)
            {
                throw ExceptionFactory.AdminModeNotEnabled(multiplexer.IncludeDetailInExceptions, RedisCommand.DEBUG, null, serverEndPoint); // close enough
            }
            var tmp = physical;
            if (tmp != null) tmp.RecordConnectionFailed(ConnectionFailureType.SocketFailure);
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
            if (!multiplexer.AllowConnect)
            {
                throw new RedisConnectionException(ConnectionFailureType.InternalFailure, "debugging");
            }
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
