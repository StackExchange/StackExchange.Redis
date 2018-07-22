using System;

namespace StackExchange.Redis.Server
{
    public readonly ref struct RedisRequest
    {   // why ref? don't *really* need it, but: these things are "in flight"
        // based on an open RawResult (which is just the detokenized ReadOnlySequence<byte>)
        // so: using "ref" makes it clear that you can't expect to store these and have
        // them keep working
        private readonly RawResult _inner;

        public int Count { get; }
        public string Command { get; }
        public override string ToString() => Command;
        public override bool Equals(object obj) => throw new NotSupportedException();

        private void AssertCountMin(int count)
        {
            if (Count < count) throw new InvalidOperationException($"Unknown subcommand or wrong number of arguments for '{Command}'.");
        }

        public RedisResult AssertCount(int count, bool asSubCommand) => Count == count ? null :
            asSubCommand ? UnknownSubcommandOrArgumentCount() : WrongArgCount();
        public RedisResult WrongArgCount() => RedisResult.Create($"ERR wrong number of arguments for '{Command}' command", ResultType.Error);

        public RedisResult UnknownSubcommandOrArgumentCount() => RedisResult.Create($"ERR Unknown subcommand or wrong number of arguments for '{Command}'.", ResultType.Error);

        public string GetString(int index)
        {
            AssertCountMin(index);
            return _inner[index].GetString();
        }
        public string GetStringLower(int index) => GetString(index).ToLowerInvariant();

        
        internal RedisResult GetResult(int index)
        {
            AssertCountMin(index);
            return RedisResult.Create(_inner[index].AsRedisValue());
        }

        public bool IsString(int index, string value) // TODO: optimize
        {
            AssertCountMin(index);
            return string.Equals(value, _inner[index].GetString(), StringComparison.OrdinalIgnoreCase);
        }

        public override int GetHashCode() => throw new NotSupportedException();
        internal RedisRequest(RawResult result)
        {
            _inner = result;
            Count = result.ItemsCount;
            Command = RespServer.ToLower(result.GetItems()[0]);
        }

        public RedisValue this[int index] => _inner[index].AsRedisValue();

        public void Recycle() => _inner.Recycle();

        public int GetInt32(int index)
        {
            AssertCountMin(index);
            return (int)_inner[index].AsRedisValue();
        }
        public long GetInt64(int index)
        {
            AssertCountMin(index);
            return (long)_inner[index].AsRedisValue();
        }

        public RedisKey GetKey(int index)
        {
            AssertCountMin(index);
            return _inner[index].AsRedisKey();
        }
        public RedisChannel GetChannel(int index, RedisChannel.PatternMode mode)
        {
            AssertCountMin(index);
            return _inner[index].AsRedisChannel(null, mode);
        }
    }
}
