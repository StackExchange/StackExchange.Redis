using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace StackExchange.Redis
{
    internal abstract class RedisBase : IRedis
    {
        internal static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        internal readonly ConnectionMultiplexer Multiplexer;
        internal readonly object? AsyncState;

        internal RedisBase(ConnectionMultiplexer multiplexer, object? asyncState)
        {
            Multiplexer = multiplexer;
            AsyncState = asyncState;
        }

        IConnectionMultiplexer IRedisAsync.Multiplexer => Multiplexer;

        internal CancellationToken GetEffectiveCancellationToken() => Multiplexer.GetEffectiveCancellationToken();

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

        public override string ToString() => Multiplexer.ToString();

        public bool TryWait(Task task) => task.Wait(Multiplexer.TimeoutMilliseconds);

        public void Wait(Task task) => Multiplexer.Wait(task);

        public T Wait<T>(Task<T> task) => Multiplexer.Wait(task);

        public void WaitAll(params Task[] tasks) => Multiplexer.WaitAll(tasks);

        internal virtual Task<T> ExecuteAsync<T>(Message? message, ResultProcessor<T>? processor, T defaultValue, ServerEndPoint? server = null)
        {
            if (message is null) return CompletedTask<T>.FromDefault(defaultValue, AsyncState);
            Multiplexer.CheckMessage(message);

            // The message already captures the ambient cancellation token when it was created,
            // so we don't need to pass it again. This ensures resent messages preserve their original cancellation context.
            return Multiplexer.ExecuteAsyncImpl<T>(message, processor, AsyncState, server, defaultValue);
        }

        internal virtual Task<T?> ExecuteAsync<T>(Message? message, ResultProcessor<T>? processor, ServerEndPoint? server = null)
        {
            if (message is null) return CompletedTask<T>.Default(AsyncState);
            Multiplexer.CheckMessage(message);

            // The message already captures the ambient cancellation token when it was created,
            // so we don't need to pass it again. This ensures resent messages preserve their original cancellation context.
            return Multiplexer.ExecuteAsyncImpl<T>(message, processor, AsyncState, server);
        }

        [return: NotNullIfNotNull("defaultValue")]
        internal virtual T? ExecuteSync<T>(Message? message, ResultProcessor<T>? processor, ServerEndPoint? server = null, T? defaultValue = default)
        {
            if (message is null) return defaultValue; // no-op
            Multiplexer.CheckMessage(message);
            return Multiplexer.ExecuteSyncImpl<T>(message, processor, server, defaultValue);
        }

        internal virtual RedisFeatures GetFeatures(in RedisKey key, CommandFlags flags, RedisCommand command, out ServerEndPoint? server)
        {
            server = Multiplexer.SelectServer(command, flags, key);
            var version = server == null ? Multiplexer.RawConfig.DefaultVersion : server.Version;
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
            var map = Multiplexer.CommandMap;
            var cancellationToken = GetEffectiveCancellationToken();
            if (map.IsAvailable(RedisCommand.PING))
                return ResultProcessor.TimingProcessor.CreateMessage(-1, flags, RedisCommand.PING, default, cancellationToken);
            if (map.IsAvailable(RedisCommand.TIME))
                return ResultProcessor.TimingProcessor.CreateMessage(-1, flags, RedisCommand.TIME, default, cancellationToken);
            if (map.IsAvailable(RedisCommand.ECHO))
                return ResultProcessor.TimingProcessor.CreateMessage(-1, flags, RedisCommand.ECHO, RedisLiterals.PING, cancellationToken);
            // as our fallback, we'll do something odd... we'll treat a key like a value, out of sheer desperation
            // note: this usually means: twemproxy/envoyproxy - in which case we're fine anyway, since the proxy does the routing
            return ResultProcessor.TimingProcessor.CreateMessage(0, flags, RedisCommand.EXISTS, (RedisValue)Multiplexer.UniqueId, cancellationToken);
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
