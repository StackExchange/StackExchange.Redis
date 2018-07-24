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

        public TypedRedisValue WrongArgCount() => TypedRedisValue.Error($"ERR wrong number of arguments for '{Command}' command");

        public TypedRedisValue UnknownSubcommandOrArgumentCount() => TypedRedisValue.Error($"ERR Unknown subcommand or wrong number of arguments for '{Command}'.");

        public string GetString(int index)
            => _inner[index].GetString();
        
        public bool IsString(int index, string value) // TODO: optimize
            => string.Equals(value, _inner[index].GetString(), StringComparison.OrdinalIgnoreCase);

        public override int GetHashCode() => throw new NotSupportedException();
        internal RedisRequest(RawResult result)
            : this(result, result.ItemsCount, result[0].GetString()) { }
        private RedisRequest(RawResult inner, int count, string command)
        {
            _inner = inner;
            Count = count;
            Command = command;
        }
        internal RedisRequest AsCommand(string command)
            => new RedisRequest(_inner, Count, command);

        
        internal void Recycle() => _inner.Recycle();

        public RedisValue GetValue(int index)
            => _inner[index].AsRedisValue();

        public int GetInt32(int index)
            => (int)_inner[index].AsRedisValue();

        public long GetInt64(int index) => (long)_inner[index].AsRedisValue();
    
        public RedisKey GetKey(int index) => _inner[index].AsRedisKey();
        
        public RedisChannel GetChannel(int index, RedisChannel.PatternMode mode)
            => _inner[index].AsRedisChannel(null, mode);
    }
}
