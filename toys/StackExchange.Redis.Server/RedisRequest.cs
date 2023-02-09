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

        public override string ToString() => Count == 0 ? "(n/a)" : GetString(0);
        public override bool Equals(object obj) => throw new NotSupportedException();

        public TypedRedisValue WrongArgCount() => TypedRedisValue.Error($"ERR wrong number of arguments for '{ToString()}' command");

        public TypedRedisValue CommandNotFound()
            => TypedRedisValue.Error($"ERR unknown command '{ToString()}'");

        public TypedRedisValue UnknownSubcommandOrArgumentCount() => TypedRedisValue.Error($"ERR Unknown subcommand or wrong number of arguments for '{ToString()}'.");

        public string GetString(int index)
            => _inner[index].GetString();

        public bool IsString(int index, string value) // TODO: optimize
            => string.Equals(value, _inner[index].GetString(), StringComparison.OrdinalIgnoreCase);

        public override int GetHashCode() => throw new NotSupportedException();
        internal RedisRequest(scoped in RawResult result)
        {
            _inner = result;
            Count = result.ItemsCount;
        }

        public RedisValue GetValue(int index)
            => _inner[index].AsRedisValue();

        public int GetInt32(int index)
            => (int)_inner[index].AsRedisValue();

        public long GetInt64(int index) => (long)_inner[index].AsRedisValue();

        public RedisKey GetKey(int index) => _inner[index].AsRedisKey();

        public RedisChannel GetChannel(int index, RedisChannel.PatternMode mode)
            => _inner[index].AsRedisChannel(null, mode);

        internal bool TryGetCommandBytes(int i, out CommandBytes command)
        {
            var payload = _inner[i].Payload;
            if (payload.Length > CommandBytes.MaxLength)
            {
                command = default;
                return false;
            }

            command = payload.IsEmpty ? default : new CommandBytes(payload);
            return true;
        }
    }
}
