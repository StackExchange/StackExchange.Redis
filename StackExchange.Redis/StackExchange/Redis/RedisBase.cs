using System;
using System.Collections.Generic;
using System.Threading;
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

        ConnectionMultiplexer IRedisAsync.Multiplexer { get { return multiplexer; } }
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

        public void Quit(CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.QUIT);
            ExecuteSync(msg, ResultProcessor.DemandOK);
        }

        public Task QuitAsync(CommandFlags flags = CommandFlags.None)
        {
            var msg = Message.Create(-1, flags, RedisCommand.QUIT);
            return ExecuteAsync(msg, ResultProcessor.DemandOK);
        }

        public override string ToString()
        {
            return multiplexer.ToString();
        }

        public bool TryWait(Task task)
        {
            return task.Wait(multiplexer.TimeoutMilliseconds);
        }

        public void Wait(Task task)
        {
            multiplexer.Wait(task);
        }

        public T Wait<T>(Task<T> task)
        {
            return multiplexer.Wait(task);
        }

        public void WaitAll(params Task[] tasks)
        {
            multiplexer.WaitAll(tasks);
        }

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

        internal virtual RedisFeatures GetFeatures(int db, RedisKey key, CommandFlags flags, out ServerEndPoint server)
        {
            server = multiplexer.SelectServer(db, RedisCommand.PING, flags, key);
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


        internal abstract class CursorEnumerableBase
        {
            internal const int DefaultPageSize = 10;
            internal static bool IsNil(RedisValue pattern)
            {
                if (pattern.IsNullOrEmpty) return true;
                if (pattern.IsInteger) return false;
                byte[] rawValue = pattern;
                return rawValue.Length == 1 && rawValue[0] == '*';
            }
        }
        internal abstract class CursorEnumerableBase<T> : CursorEnumerableBase, IEnumerable<T>, IScanning
        {
            private readonly RedisBase redis;
            private readonly ServerEndPoint server;
            protected readonly int db;
            protected readonly CommandFlags flags;
            protected readonly int pageSize;
            protected readonly long initialCursor;

            protected CursorEnumerableBase(RedisBase redis, ServerEndPoint server, int db, int pageSize, long cursor, CommandFlags flags)
            {
                this.redis = redis;
                this.server = server;
                this.db = db;
                this.pageSize = pageSize;
                this.flags = flags;
                this.initialCursor = cursor;
            }

            public IEnumerator<T> GetEnumerator()
            {
                return new CursorEnumerator(this);
            }
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
            internal struct ScanResult
            {
                public readonly long Cursor;
                public readonly T[] Values;
                public ScanResult(long cursor, T[] values)
                {
                    this.Cursor = cursor;
                    this.Values = values;
                }
            }

            protected abstract Message CreateMessage(long cursor);

            private long currentCursor, nextCursor;

            internal void SetPosition(long current, long next)
            {
                Interlocked.Exchange(ref currentCursor, current);
                Interlocked.Exchange(ref nextCursor, next);
            }

            protected abstract ResultProcessor<ScanResult> Processor { get; }

            protected ScanResult GetNextPageSync(long cursor)
            {
                return redis.ExecuteSync(CreateMessage(cursor), Processor, server);
            }
            protected Task<ScanResult> GetNextPageAsync(long cursor)
            {
                return redis.ExecuteAsync(CreateMessage(cursor), Processor, server);
            }
            protected ScanResult Wait(Task<ScanResult> pending)
            {
                return redis.Wait(pending);
            }

            class CursorEnumerator : IEnumerator<T>, IScanning
            {
                private CursorEnumerableBase<T> parent;
                public CursorEnumerator(CursorEnumerableBase<T> parent)
                {
                    if (parent == null) throw new ArgumentNullException("parent");
                    this.parent = parent;
                    Reset();
                }
                public T Current
                {
                    get { return page[pageIndex]; }
                }

                void IDisposable.Dispose() { parent = null; state = State.Disposed; }

                object System.Collections.IEnumerator.Current
                {
                    get { return page[pageIndex]; ; }
                }

                private bool SimpleNext()
                {
                    if (page != null && ++pageIndex < page.Length)
                    {
                        // first of a new page? cool; start a new background op, because we're about to exit the iterator
                        if (pageIndex == 0 && pending == null && nextCursor != 0)
                        {
                            pending = parent.GetNextPageAsync(nextCursor);
                        }
                        return true;
                    }
                    return false;
                }

                T[] page;
                Task<ScanResult> pending;
                int pageIndex;
                private long currentCursor, nextCursor;

                private State state;
                private enum State : byte
                {
                    Initial,
                    Running,
                    Complete,
                    Disposed,
                }

                void ProcessReply(ScanResult result)
                {
                    pending = null;
                    page = result.Values;
                    pageIndex = -1;
                    parent.SetPosition(currentCursor = nextCursor, nextCursor = result.Cursor);
                }

                public bool MoveNext()
                {
                    switch(state)
                    {
                        case State.Complete:
                            return false;
                        case State.Initial:
                            ProcessReply(parent.GetNextPageSync(nextCursor));
                            state = State.Running;
                            goto case State.Running;
                        case State.Running:
                            // are we working through the current buffer?
                            if (SimpleNext()) return true;

                            // do we have an outstanding operation? wait for the background task to finish
                            while (pending != null)
                            {
                                ProcessReply(parent.Wait(pending));
                                if (SimpleNext()) return true;
                            }

                            // nothing outstanding? wait synchronously
                            while(nextCursor != 0)
                            {
                                ProcessReply(parent.GetNextPageSync(nextCursor));
                                if (SimpleNext()) return true;
                            }

                            // we're exhausted
                            state = State.Complete;
                            return false;
                        case State.Disposed:
                        default:
                            throw new ObjectDisposedException(GetType().Name);
                    }
                }

                public void Reset()
                {
                    if(state == State.Disposed) throw new ObjectDisposedException(GetType().Name);
                    nextCursor = currentCursor = parent.initialCursor;
                    state = State.Initial;
                    page = null;
                    pageIndex = -1;
                    pending = null;
                }

                long IScanning.CurrentCursor
                {
                    get { return currentCursor; }
                }

                long IScanning.NextCursor
                {
                    get { return nextCursor; }
                }

                int IScanning.PageSize
                {
                    get { return parent.pageSize; }
                }
            }

            long IScanning.CurrentCursor
            {
                get { return Interlocked.Read(ref currentCursor); }
            }

            long IScanning.NextCursor
            {
                get { return Interlocked.Read(ref nextCursor); }
            }

            int IScanning.PageSize
            {
                get { return pageSize; }
            }
        }
    }
}
