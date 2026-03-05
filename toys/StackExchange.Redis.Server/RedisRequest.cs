using System;

namespace StackExchange.Redis.Server
{
    public readonly ref struct RedisRequest
    {
        // why ref? don't *really* need it, but: these things are "in flight"
        // based on an open RawResult (which is just the detokenized ReadOnlySequence<byte>)
        // so: using "ref" makes it clear that you can't expect to store these and have
        // them keep working
        private readonly RawResult _inner;
        private readonly RedisClient _client;

        public RedisRequest WithClient(RedisClient client) => new(in this, client);

        private RedisRequest(scoped in RedisRequest original, RedisClient client)
        {
            this = original;
            _client = client;
        }
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

        public bool TryGetInt64(int index, out long value)
            => _inner[index].TryGetInt64(out value);
        public bool TryGetInt32(int index, out int value)
        {
            if (_inner[index].TryGetInt64(out var tmp))
            {
                value = (int)tmp;
                if (value == tmp) return true;
            }

            value = 0;
            return false;
        }

        public long GetInt64(int index) => (long)_inner[index].AsRedisValue();

        public RedisKey GetKey(int index, KeyFlags flags = KeyFlags.None)
        {
            var key = _inner[index].AsRedisKey();
            _client?.OnKey(key, flags);
            return key;
        }

        internal RedisChannel GetChannel(int index, RedisChannel.RedisChannelOptions options)
            => _inner[index].AsRedisChannel(null, options);

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

    [Flags]
    public enum KeyFlags
    {
        None = 0,
        ReadOnly = 1 << 0,
        NoSlotCheck = 1 << 1,
    }
}
