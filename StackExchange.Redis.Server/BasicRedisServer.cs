using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;

namespace StackExchange.Redis.Server
{
    public abstract class BasicRedisServer : RedisServer
    {
        protected BasicRedisServer(TextWriter output = null) : base(output)
        {
            var config = ServerConfiguration;
            config["timeout"] = "0";
            config["slave-read-only"] = "yes";
        }
        public abstract int Databases { get; }

        public override RedisResult OnExecute(RedisClient client, RedisRequest request)
        {
            switch(request.Command)
            {
                case "client": return Client(client, request);
                case "cluster": return Cluster(client, request);
                case "config": return Config(client, request);
                case "dbsize": return Dbsize(client, request);
                case "del": return Del(client, request);
                case "echo": return Echo(client, request);
                case "flushall": return Flushall(client, request);
                case "flushdb": return Flushdb(client, request);
                case "get": return Get(client, request);
                case "info": return Info(client, request);
                case "ping": return Ping(client, request);
                case "quit": return Quit(client, request);
                case "select": return Select(client, request);
                case "set": return Set(client, request);
                case "subscribe": return Subscribe(client, request);
                case "unsubscribe": return Unsubscribe(client, request);
                default: return null;
            }
        }



        protected virtual RedisResult Client(RedisClient client, RedisRequest request)
        {
            var subcommand = request.GetStringLower(1);
            switch(subcommand.ToLowerInvariant())
            {
                case "setname":
                    var chk = request.AssertCount(3, true);
                    if (chk != null) return chk;
                    client.Name = request.GetString(2);
                    return RedisResult.OK;
                case "getname":
                    return request.AssertCount(2, true) ?? RedisResult.Create(client.Name, ResultType.BulkString);
            }
            return request.UnknownSubcommandOrArgumentCount();
        }

        protected virtual RedisResult Cluster(RedisClient client, RedisRequest request)
            => CommandNotFound(request.Command);

        protected virtual void OnUpdateServerConfiguration()
        {
            var config = ServerConfiguration;
            config["databases"] = Databases.ToString();
        }
        protected RedisConfig ServerConfiguration { get; } = RedisConfig.Create();
        protected struct RedisConfig
        {
            internal static RedisConfig Create() => new RedisConfig(
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
            
            internal Dictionary<string, string> Wrapped { get; }
            public int Count => Wrapped.Count;

            private RedisConfig(Dictionary<string, string> inner) => Wrapped = inner;
            public string this[string key]
            {
                get => Wrapped.TryGetValue(key, out var val) ? val : null;
                set
                {
                    if (Wrapped.ContainsKey(key)) Wrapped[key] = value; // no need to fix case
                    else Wrapped[key.ToLowerInvariant()] = value;
                }
            }

            internal int CountMatch(string pattern)
            {
                int count = 0;
                foreach(var pair in Wrapped)
                {
                    if (IsMatch(pattern, pair.Key)) count++;
                }
                return count;
            }

            internal static bool IsMatch(string pattern, string key)
            {
                // non-trivial wildcards not implemented yet!
                return pattern == "*" || string.Equals(pattern, key, StringComparison.OrdinalIgnoreCase);
            }
        }
        protected virtual RedisResult Config(RedisClient client, RedisRequest request)
        {
            var subcommand = request.GetStringLower(1);
            switch(subcommand)
            {
                case "get":
                    var chk = request.AssertCount(3, true);
                    if (chk != null) return chk;

                    var pattern = request.GetString(2);

                    OnUpdateServerConfiguration();
                    var config = ServerConfiguration;
                    var matches = config.CountMatch(pattern);
                    if (matches == 0) return RedisResult.Create(Array.Empty<RedisValue>());

                    var arr = new RedisValue[2 * matches];
                    int index = 0;
                    foreach(var pair in config.Wrapped)
                    {
                        if (RedisConfig.IsMatch(pattern, pair.Key))
                        {
                            arr[index++] = pair.Key;
                            arr[index++] = pair.Value;
                        }
                    }
                    if (index != arr.Length) throw new InvalidOperationException("Configuration CountMatch fail");
                    return RedisResult.Create(arr);
            }
            return request.UnknownSubcommandOrArgumentCount();
        }

        protected virtual RedisResult Echo(RedisClient client, RedisRequest request)
            => request.AssertCount(2, false) ?? request.GetResult(1);

        protected virtual RedisResult Get(RedisClient client, RedisRequest request)
        {
            return request.AssertCount(2, false)
                ?? RedisResult.Create(Get(client.Database, request.GetKey(1)));
        }

        protected virtual RedisValue Get(int database, RedisKey key) => throw new NotImplementedException();

        protected virtual RedisResult Set(RedisClient client, RedisRequest request)
        {
            var chk = request.AssertCount(3, false);
            if (chk != null) return chk;
            Set(client.Database, request.GetKey(1), request[2]);
            return RedisResult.OK;
        }
        protected virtual void Set(int database, RedisKey key, RedisValue value) => throw new NotImplementedException();

        protected virtual RedisResult Del(RedisClient client, RedisRequest request)
        {
            int count = 0;
            for(int i = 1; i < request.Count; i++)
            {
                if (Del(client.Database, request.GetKey(i)))
                    count++;
            }
            return RedisResult.Create(count, ResultType.Integer);
        }
        protected virtual bool Del(int database, RedisKey key) => throw new NotImplementedException();

        protected virtual RedisResult Dbsize(RedisClient client, RedisRequest request)
        {
            return request.AssertCount(1, false) ??
                RedisResult.Create(Dbsize(client.Database), ResultType.Integer);
        }
        protected virtual long Dbsize(int database) => throw new NotImplementedException();

        protected virtual RedisResult Flushall(RedisClient client, RedisRequest request)
        {
            var chk = request.AssertCount(1, false);
            if (chk != null) return chk;

            var count = Databases;
            for(int i = 0; i < count; i++)
            {
                Flushdb(i);
            }
            return RedisResult.OK;
        }

        protected virtual RedisResult Flushdb(RedisClient client, RedisRequest request)
        {
            var chk = request.AssertCount(1, false);
            if (chk != null) return chk;

            Flushdb(client.Database);
            return RedisResult.OK;
        }
        protected virtual void Flushdb(int database) => throw new NotImplementedException();


        protected virtual RedisResult Info(RedisClient client, RedisRequest request)
        {
            if (request.Count > 2) return RedisResult.Create("ERR syntax error", ResultType.Error);
            return RedisResult.Create("", ResultType.BulkString);
        }
        protected virtual RedisResult Ping(RedisClient client, RedisRequest request)
        {
            if (request.Count == 1) return RedisResult.Create("PONG", ResultType.SimpleString);

            return request.AssertCount(2, false) ?? RedisResult.Create(request.GetString(1), ResultType.SimpleString);
        }
        protected virtual RedisResult Quit(RedisClient client, RedisRequest request)
        {
            var chk = request.AssertCount(1, false);
            if (chk != null) return chk;

            client.Closed = true;
            RemoveClient(client);
            return RedisResult.OK;
        }
        protected virtual RedisResult Select(RedisClient client, RedisRequest request)
        {
            var chk = request.AssertCount(2, false);
            if (chk != null) return chk;
            var raw = request[1];
            if (!raw.IsInteger) return RedisResult.Create("ERR invalid DB index", ResultType.Error);
            int db = (int)raw;
            if (db < 0 || db >= Databases) return RedisResult.Create("ERR DB index is out of range", ResultType.Error);
            client.Database = db;
            return RedisResult.OK;
        }

        protected virtual RedisResult Subscribe(RedisClient client, RedisRequest request)
            => SubscribeImpl(client, request);
        protected virtual RedisResult Unsubscribe(RedisClient client, RedisRequest request)
            => SubscribeImpl(client, request);
        private RedisResult SubscribeImpl(RedisClient client, RedisRequest request)
        {
            if (request.Count < 2) return request.UnknownSubcommandOrArgumentCount();

            RedisValue[] reply = new RedisValue[3 * (request.Count - 1)];
            int index = 0;
            var mode = request.Command[0] == 'p' ? RedisChannel.PatternMode.Pattern : RedisChannel.PatternMode.Literal;
            for (int i = 1; i < request.Count; i++)
            {
                var channel = request.GetChannel(i, mode);
                int count;
                switch(request.Command)
                {
                    case "subscribe": count = client.Subscribe(channel); break;
                    case "unsubscribe": count = client.Unsubscribe(channel); break;
                    default: return null;
                }
                reply[index++] = request.Command;
                reply[index++] = (byte[])channel;
                reply[index++] = count;
            }
            return RedisResult.Create(reply);
        }
    }
}
