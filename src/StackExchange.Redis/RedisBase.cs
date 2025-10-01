using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace StackExchange.Redis
{
    internal abstract partial class RedisBase : IRedis
    {
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

        public void Wait(Task task) => multiplexer.Wait(task);

        public T Wait<T>(Task<T> task) => multiplexer.Wait(task);

        public void WaitAll(params Task[] tasks) => multiplexer.WaitAll(tasks);

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

internal static class WhenExtensions
{
    internal static void AlwaysOnly(this When when)
    {
        if (when != When.Always) Throw(when);
        static void Throw(When when) => throw new ArgumentException(when + " is not valid in this context; the permitted values are: Always");
    }

    internal static void AlwaysOrExists(this When when)
    {
        switch (when)
        {
            case When.Always:
            case When.Exists:
                break;
            default:
                Throw(when);
                break;
        }
        static void Throw(When when) => throw new ArgumentException(when + " is not valid in this context; the permitted values are: Always, Exists");
    }

    internal static void AlwaysOrExistsOrNotExists(this When when)
    {
        switch (when)
        {
            case When.Always:
            case When.Exists:
            case When.NotExists:
                break;
            default:
                Throw(when);
                break;
        }
        static void Throw(When when)
            => throw new ArgumentException(when + " is not valid in this context; the permitted values are: Always, Exists, NotExists");
    }

    internal static void AlwaysOrNotExists(this When when)
    {
        switch (when)
        {
            case When.Always:
            case When.NotExists:
                break;
            default:
                Throw(when);
                break;
        }
        static void Throw(When when) => throw new ArgumentException(when + " is not valid in this context; the permitted values are: Always, NotExists");
    }
}
