using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

namespace StackExchange.Redis
{
    internal abstract partial class RedisBase : IRedis
    {
        /// <summary>The context commands are composed and sent through.</summary>
        /// <remarks>
        /// Not yet implemented for connection-backed types. The context surface is being brought up
        /// against a minimal implementation first (<c>RespDatabase</c>); wiring it to a live multiplexer
        /// means routing a rendered frame through the existing message pipeline, which is separate work.
        /// </remarks>
        public Interpolated.RespContext Context
            => throw new NotImplementedException(
                "The context surface is not yet wired to a live connection; see RespDatabase.");

        /// <summary>Lets the context surface ask what the receiving server can do.</summary>
        /// <remarks>
        /// Lives here rather than on <c>RedisDatabase</c> because a server context wants it too, and wants
        /// it <i>more</i>: <c>RedisServer.GetFeatures</c> answers from its own endpoint, so the probe
        /// reports an observation rather than the guess a database has to make before a server is selected.
        /// </remarks>
        internal sealed class ServerFeatureProbe(RedisBase target) : Interpolated.IRespServerFeatures
        {
            public bool TryGetFeatures(RedisCommand command, in RedisKey key, CommandFlags flags, out RedisFeatures features)
            {
                features = target.GetFeatures(key, flags, command, out var server);

                // the features are always usable - GetFeatures falls back to the configured default
                // version - but only a selected server makes them an observation rather than a guess
                return server is not null;
            }
        }

        internal static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        internal readonly ConnectionMultiplexer multiplexer;
        protected readonly object? asyncState;

        internal RedisBase(ConnectionMultiplexer multiplexer, object? asyncState)
        {
            this.multiplexer = multiplexer;
            this.asyncState = asyncState;
        }

        IConnectionMultiplexer IRedisAsync.Multiplexer => multiplexer;

        public virtual TimeSpan Ping(CommandFlags flags = CommandFlags.None)
        {
            var msg = GetTimerMessage(flags);
            return ExecuteSync(msg, ResultProcessor.ResponseTimer);
        }

        public virtual Task<TimeSpan> PingAsync(CommandFlags flags = CommandFlags.None)
        {
            var msg = GetTimerMessage(flags);
            return ExecuteAsync(msg, ResultProcessor.ResponseTimer);
        }

        public override string ToString() => multiplexer.ToString();

        public bool TryWait(Task task) => task.Wait(multiplexer.TimeoutMilliseconds);

        // forwarding is not using: these decorators must implement the interface in full, and the
        // implementation cannot be dropped while the interface declares it
        #pragma warning disable SER308 // Blocking on a task through the library's Wait helpers
        public void Wait(Task task) => multiplexer.Wait(task);

        public T Wait<T>(Task<T> task) => multiplexer.Wait(task);

        public void WaitAll(params Task[] tasks) => multiplexer.WaitAll(tasks);
        #pragma warning restore SER308

        internal virtual Task<T> ExecuteAsync<T>(Message? message, ResultProcessor<T>? processor, T defaultValue, ServerEndPoint? server = null)
        {
            if (message is null) return CompletedTask<T>.FromDefault(defaultValue, asyncState);
            multiplexer.CheckMessage(message);
            return multiplexer.ExecuteAsyncImpl<T>(message, processor, asyncState, server, defaultValue);
        }

        internal virtual Task<T?> ExecuteAsync<T>(Message? message, ResultProcessor<T>? processor, ServerEndPoint? server = null)
        {
            if (message is null) return CompletedTask<T>.Default(asyncState);
            multiplexer.CheckMessage(message);
            return multiplexer.ExecuteAsyncImpl<T>(message, processor, asyncState, server);
        }

        [return: NotNullIfNotNull("defaultValue")]
        internal virtual T? ExecuteSync<T>(Message? message, ResultProcessor<T>? processor, ServerEndPoint? server = null, T? defaultValue = default)
        {
            if (message is null) return defaultValue; // no-op
            multiplexer.CheckMessage(message);
            return multiplexer.ExecuteSyncImpl<T>(message, processor, server, defaultValue);
        }

        internal virtual RedisFeatures GetFeatures(in RedisKey key, CommandFlags flags, RedisCommand command, out ServerEndPoint? server)
        {
            server = multiplexer.SelectServer(command, flags, key);
            var version = server == null ? multiplexer.RawConfig.DefaultVersion : server.Version;
            return new RedisFeatures(version);
        }

        protected static void WhenAlwaysOrExists(When when)
        {
            switch (when)
            {
                case When.Always:
                case When.Exists:
                    break;
                default:
                    throw new ArgumentException(when + " is not valid in this context; the permitted values are: Always, Exists");
            }
        }

        protected static void WhenAlwaysOrExistsOrNotExists(When when)
        {
            switch (when)
            {
                case When.Always:
                case When.Exists:
                case When.NotExists:
                    break;
                default:
                    throw new ArgumentException(when + " is not valid in this context; the permitted values are: Always, Exists, NotExists");
            }
        }

        protected static void WhenAlwaysOrNotExists(When when)
        {
            switch (when)
            {
                case When.Always:
                case When.NotExists:
                    break;
                default:
                    throw new ArgumentException(when + " is not valid in this context; the permitted values are: Always, NotExists");
            }
        }

        private ResultProcessor.TimingProcessor.TimerMessage GetTimerMessage(CommandFlags flags)
        {
            // do the best we can with available commands
            var map = multiplexer.CommandMap;
            if (map.IsAvailable(RedisCommand.PING))
                return ResultProcessor.TimingProcessor.CreateMessage(-1, flags, RedisCommand.PING);
            if (map.IsAvailable(RedisCommand.TIME))
                return ResultProcessor.TimingProcessor.CreateMessage(-1, flags, RedisCommand.TIME);
            if (map.IsAvailable(RedisCommand.ECHO))
                return ResultProcessor.TimingProcessor.CreateMessage(-1, flags, RedisCommand.ECHO, RedisLiterals.PING);
            // as our fallback, we'll do something odd... we'll treat a key like a value, out of sheer desperation
            // note: this usually means: twemproxy/envoyproxy - in which case we're fine anyway, since the proxy does the routing
            return ResultProcessor.TimingProcessor.CreateMessage(0, flags, RedisCommand.EXISTS, (RedisValue)multiplexer.UniqueId);
        }

        internal static class CursorUtils
        {
            internal const long Origin = 0;
            internal const int
                DefaultRedisPageSize = 10,
                DefaultLibraryPageSize = 250;
            internal static bool IsNil(in RedisValue pattern)
            {
                if (pattern.IsNullOrEmpty) return true;
                if (pattern.IsInteger) return false;
                byte[] rawValue = pattern!;
                return rawValue.Length == 1 && rawValue[0] == '*';
            }
        }
    }
}
