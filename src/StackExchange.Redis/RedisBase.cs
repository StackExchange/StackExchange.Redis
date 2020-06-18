using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace StackExchange.Redis
{
    internal abstract partial class RedisBase : IRedis
    {
        internal static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        internal readonly ConnectionMultiplexer multiplexer;
        protected readonly object asyncState;

        internal RedisBase(ConnectionMultiplexer multiplexer, object asyncState)
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

        public void Wait(Task task) => multiplexer.Wait(task);

        public T Wait<T>(Task<T> task) => multiplexer.Wait(task);

        public void WaitAll(params Task[] tasks) => multiplexer.WaitAll(tasks);

        internal virtual Task<T> ExecuteAsync<T>(Message message, ResultProcessor<T> processor, ServerEndPoint server = null)
        {
            if (message == null) return CompletedTask<T>.Default(asyncState);
            multiplexer.CheckMessage(message);
            return multiplexer.ExecuteAsyncImpl<T>(message, processor, asyncState, server);
        }

        internal virtual T ExecuteSync<T>(Message message, ResultProcessor<T> processor, ServerEndPoint server = null)
        {
            if (message == null) return default(T); // no-op
            multiplexer.CheckMessage(message);
            return multiplexer.ExecuteSyncImpl<T>(message, processor, server);
        }

        internal virtual RedisFeatures GetFeatures(in RedisKey key, CommandFlags flags, out ServerEndPoint server)
        {
            server = multiplexer.SelectServer(RedisCommand.PING, flags, key);
            var version = server == null ? multiplexer.RawConfig.DefaultVersion : server.Version;
            return new RedisFeatures(version);
        }

        protected void WhenAlwaysOrExists(When when)
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

        protected void WhenAlwaysOrExistsOrNotExists(When when)
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

        protected void WhenAlwaysOrNotExists(When when)
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
            if(map.IsAvailable(RedisCommand.PING))
                return ResultProcessor.TimingProcessor.CreateMessage(-1, flags, RedisCommand.PING);
            if(map.IsAvailable(RedisCommand.TIME))
                return ResultProcessor.TimingProcessor.CreateMessage(-1, flags, RedisCommand.TIME);
            if (map.IsAvailable(RedisCommand.ECHO))
                return ResultProcessor.TimingProcessor.CreateMessage(-1, flags, RedisCommand.ECHO, RedisLiterals.PING);
            // as our fallback, we'll do something odd... we'll treat a key like a value, out of sheer desperation
            // note: this usually means: twemproxy - in which case we're fine anyway, since the proxy does the routing
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
                byte[] rawValue = pattern;
                return rawValue.Length == 1 && rawValue[0] == '*';
            }
        }
    }
}
