using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace StackExchange.Redis.StackExchange.Redis.KeyspaceIsolation
{
    internal class RedisWrapperBase<TInner> : IRedisAsync where TInner : IRedisAsync
    {
        private readonly TInner _inner;
        private readonly RedisKey _prefix;

        internal RedisWrapperBase(TInner inner, RedisKey prefix)
        {
            _inner = inner;
            _prefix = prefix;
        }

        internal TInner Inner
        {
            get { return _inner; }
        }

        internal RedisKey Prefix
        {
            get { return _prefix; }
        }

        public ConnectionMultiplexer Multiplexer
        {
            get { return this.Inner.Multiplexer; }
        }

        public Task<TimeSpan> PingAsync(CommandFlags flags = CommandFlags.None)
        {
            return this.Inner.PingAsync(flags);
        }

        public bool TryWait(Task task)
        {
            return this.Inner.TryWait(task);
        }

        public TResult Wait<TResult>(Task<TResult> task)
        {
            return this.Inner.Wait(task);
        }

        public void Wait(Task task)
        {
            this.Inner.Wait(task);
        }

        public void WaitAll(params Task[] tasks)
        {
            this.Inner.WaitAll(tasks);
        }

#if DEBUG
        public Task<string> ClientGetNameAsync(CommandFlags flags = CommandFlags.None)
        {
            return this.Inner.ClientGetNameAsync(flags);
        }
#endif

        protected internal RedisKey ToInner(RedisKey outer)
        {
            return this.Prefix + outer;
        }

        protected RedisKey ToInnerOrDefault(RedisKey outer)
        {
            if (outer == default(RedisKey))
            {
                return outer;
            }
            else
            {
                return this.ToInner(outer);
            }
        }

        protected RedisKey[] ToInner(RedisKey[] outer)
        {
            if (outer == null || outer.Length == 0)
            {
                return outer;
            }
            else
            {
                RedisKey[] inner = new RedisKey[outer.Length];

                for (int i = 0; i < outer.Length; ++i)
                {
                    inner[i] = this.ToInner(outer[i]);
                }

                return inner;
            }
        }

        protected KeyValuePair<RedisKey, RedisValue> ToInner(KeyValuePair<RedisKey, RedisValue> outer)
        {
            return new KeyValuePair<RedisKey, RedisValue>(this.ToInner(outer.Key), outer.Value);
        }

        protected KeyValuePair<RedisKey, RedisValue>[] ToInner(KeyValuePair<RedisKey, RedisValue>[] outer)
        {
            if (outer == null || outer.Length == 0)
            {
                return outer;
            }
            else
            {
                KeyValuePair<RedisKey, RedisValue>[] inner = new KeyValuePair<RedisKey, RedisValue>[outer.Length];

                for (int i = 0; i < outer.Length; ++i)
                {
                    inner[i] = this.ToInner(outer[i]);
                }

                return inner;
            }
        }

        protected RedisValue ToInner(RedisValue outer)
        {
            return RedisKey.Concatenate(this.Prefix, outer);
        }

        protected RedisValue SortByToInner(RedisValue outer)
        {
            if (outer == "nosort")
            {
                return outer;
            }
            else
            {
                return this.ToInner(outer);
            }
        }

        protected RedisValue SortGetToInner(RedisValue outer)
        {
            if (outer == "#")
            {
                return outer;
            }
            else
            {
                return this.ToInner(outer);
            }
        }

        protected RedisValue[] SortGetToInner(RedisValue[] outer)
        {
            if (outer == null || outer.Length == 0)
            {
                return outer;
            }
            else
            {
                RedisValue[] inner = new RedisValue[outer.Length];

                for (int i = 0; i < outer.Length; ++i)
                {
                    inner[i] = this.SortGetToInner(outer[i]);
                }

                return inner;
            }
        }

        protected RedisChannel ToInner(RedisChannel outer)
        {
            return RedisKey.Concatenate((byte[])Prefix, (byte[])outer);
        }

        private Func<RedisKey, RedisKey> mapFunction;
        protected Func<RedisKey, RedisKey> GetMapFunction()
        {
            // create as a delegate when first required, then re-use
            return mapFunction ?? (mapFunction = new Func<RedisKey, RedisKey>(this.ToInner));
        }
    }
}
