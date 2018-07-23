using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace StackExchange.Redis.Server
{
    public abstract class RedisServer : RespServer
    {
        public static bool IsMatch(string pattern, string key)
        {
            // non-trivial wildcards not implemented yet!
            return pattern == "*" || string.Equals(pattern, key, StringComparison.OrdinalIgnoreCase);
        }
        protected RedisServer(int databases = 16, TextWriter output = null) : base(output)
        {
            if (databases < 1) throw new ArgumentOutOfRangeException(nameof(databases));
            var config = ServerConfiguration;
            config["timeout"] = "0";
            config["slave-read-only"] = "yes";
            config["databases"] = databases.ToString();
            config["slaveof"] = "";
        }
        public int Databases { get; }

        [RedisCommand(-3)]
        protected virtual RedisResult Sadd(RedisClient client, RedisRequest request)
        {
            int added = 0;
            var key = request.GetKey(1);
            for (int i = 2; i < request.Count; i++)
            {
                if (Sadd(client.Database, key, request.GetValue(i)))
                    added++;
            }
            return RedisResult.Create(added, ResultType.Integer);
        }
        protected virtual bool Sadd(int database, RedisKey key, RedisValue value) => throw new NotSupportedException();

        [RedisCommand(-3)]
        protected virtual RedisResult Srem(RedisClient client, RedisRequest request)
        {
            int removed = 0;
            var key = request.GetKey(1);
            for (int i = 2; i < request.Count; i++)
            {
                if (Srem(client.Database, key, request.GetValue(i)))
                    removed++;
            }
            return RedisResult.Create(removed, ResultType.Integer);
        }
        protected virtual bool Srem(int database, RedisKey key, RedisValue value) => throw new NotSupportedException();

        [RedisCommand(2)]
        protected virtual RedisResult Spop(RedisClient client, RedisRequest request)
            => RedisResult.Create(Spop(client.Database, request.GetKey(1)), ResultType.BulkString);

        protected virtual RedisValue Spop(int database, RedisKey key) => throw new NotSupportedException();

        [RedisCommand(2)]
        protected virtual RedisResult Scard(RedisClient client, RedisRequest request)
            => RedisResult.Create(Scard(client.Database, request.GetKey(1)), ResultType.Integer);

        protected virtual long Scard(int database, RedisKey key) => throw new NotSupportedException();

        [RedisCommand(3)]
        protected virtual RedisResult Sismember(RedisClient client, RedisRequest request)
            => Sismember(client.Database, request.GetKey(1), request.GetValue(2)) ? RedisResult.One : RedisResult.Zero;

        protected virtual bool Sismember(int database, RedisKey key, RedisValue value) => throw new NotSupportedException();

        [RedisCommand(3, "client", "setname", LockFree = true)]
        protected virtual RedisResult ClientSetname(RedisClient client, RedisRequest request)
        {
            client.Name = request.GetString(2);
            return RedisResult.OK;
        }

        [RedisCommand(2, "client", "getname", LockFree = true)]
        protected virtual RedisResult ClientGetname(RedisClient client, RedisRequest request)
            => RedisResult.Create(client.Name, ResultType.BulkString);

        [RedisCommand(3, "client", "reply", LockFree = true)]
        protected virtual RedisResult ClientReply(RedisClient client, RedisRequest request)
        {
            if (request.IsString(2, "on")) client.SkipReplies = -1; // reply to nothing
            else if (request.IsString(2, "off")) client.SkipReplies = 0; // reply to everything
            else if (request.IsString(2, "skip")) client.SkipReplies = 2; // this one, and the next one
            else return RedisResult.Create("ERR syntax error", ResultType.Error);
            return RedisResult.OK;
        }

        [RedisCommand(-1)]
        protected virtual RedisResult Cluster(RedisClient client, RedisRequest request)
            => CommandNotFound(request.Command);

        [RedisCommand(-3)]
        protected virtual RedisResult Lpush(RedisClient client, RedisRequest request)
        {
            var key = request.GetKey(1);
            long length = -1;
            for (int i = 2; i < request.Count; i++)
            {
                length = Lpush(client.Database, key, request.GetValue(i));
            }
            return RedisResult.Create(length, ResultType.Integer);
        }

        [RedisCommand(-3)]
        protected virtual RedisResult Rpush(RedisClient client, RedisRequest request)
        {
            var key = request.GetKey(1);
            long length = -1;
            for (int i = 2; i < request.Count; i++)
            {
                length = Rpush(client.Database, key, request.GetValue(i));
            }
            return RedisResult.Create(length, ResultType.Integer);
        }

        [RedisCommand(2)]
        protected virtual RedisResult Lpop(RedisClient client, RedisRequest request)
            => RedisResult.Create(Lpop(client.Database, request.GetKey(1)), ResultType.BulkString);

        [RedisCommand(2)]
        protected virtual RedisResult Rpop(RedisClient client, RedisRequest request)
            => RedisResult.Create(Rpop(client.Database, request.GetKey(1)), ResultType.BulkString);

        [RedisCommand(2)]
        protected virtual RedisResult Llen(RedisClient client, RedisRequest request)
            => RedisResult.Create(Llen(client.Database, request.GetKey(1)), ResultType.Integer);

        protected virtual long Lpush(int database, RedisKey key, RedisValue value) => throw new NotSupportedException();
        protected virtual long Rpush(int database, RedisKey key, RedisValue value) => throw new NotSupportedException();
        protected virtual long Llen(int database, RedisKey key) => throw new NotSupportedException();
        protected virtual RedisValue Rpop(int database, RedisKey key) => throw new NotSupportedException();
        protected virtual RedisValue Lpop(int database, RedisKey key) => throw new NotSupportedException();

        [RedisCommand(4)]
        protected virtual RedisResult LRange(RedisClient client, RedisRequest request)
        {
            var key = request.GetKey(1);
            long start = request.GetInt64(2), stop = request.GetInt64(3);

            var len = Llen(client.Database, key);
            if (len == 0) return RedisResult.EmptyArray;

            if (start < 0) start = len + start;
            if (stop < 0) stop = len + stop;

            if (stop < 0 || start >= len || stop < start) return RedisResult.EmptyArray;

            if (start < 0) start = 0;
            else if (start >= len) start = len - 1;

            if (stop < 0) stop = 0;
            else if (stop >= len) stop = len - 1;

            var arr = new RedisValue[(stop - start) + 1];
            LRange(client.Database, key, start, arr);
            return RedisResult.Create(arr);
        }
        protected virtual void LRange(int database, RedisKey key, long start, RedisValue[] arr) => throw new NotSupportedException();

        protected virtual void OnUpdateServerConfiguration() { }
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
                foreach (var pair in Wrapped)
                {
                    if (IsMatch(pattern, pair.Key)) count++;
                }
                return count;
            }
        }
        [RedisCommand(3, "config", "get", LockFree = true)]
        protected virtual RedisResult Config(RedisClient client, RedisRequest request)
        {
            var pattern = request.GetString(2);

            OnUpdateServerConfiguration();
            var config = ServerConfiguration;
            var matches = config.CountMatch(pattern);
            if (matches == 0) return RedisResult.Create(Array.Empty<RedisResult>());

            var arr = new RedisResult[2 * matches];
            int index = 0;
            foreach (var pair in config.Wrapped)
            {
                if (IsMatch(pattern, pair.Key))
                {
                    arr[index++] = RedisResult.Create(pair.Key, ResultType.BulkString);
                    arr[index++] = RedisResult.Create(pair.Value, ResultType.BulkString);
                }
            }
            if (index != arr.Length) throw new InvalidOperationException("Configuration CountMatch fail");
            return RedisResult.Create(arr);
        }

        [RedisCommand(2, LockFree = true)]
        protected virtual RedisResult Echo(RedisClient client, RedisRequest request)
            => request.GetResult(1);

        [RedisCommand(2)]
        protected virtual RedisResult Exists(RedisClient client, RedisRequest request)
        {
            int count = 0;
            var db = client.Database;
            for (int i = 1; i < request.Count; i++)
            {
                if (Exists(db, request.GetKey(i)))
                    count++;
            }
            return RedisResult.Create(count, ResultType.Integer);
        }

        protected virtual bool Exists(int database, RedisKey key)
        {
            try
            {
                return !Get(database, key).IsNull;
            }
            catch (InvalidCastException) { return true; } // to be an invalid cast, it must exist
        }

        [RedisCommand(2)]
        protected virtual RedisResult Get(RedisClient client, RedisRequest request)
            => RedisResult.Create(Get(client.Database, request.GetKey(1)), ResultType.BulkString);

        protected virtual RedisValue Get(int database, RedisKey key) => throw new NotSupportedException();

        [RedisCommand(3)]
        protected virtual RedisResult Set(RedisClient client, RedisRequest request)
        {
            Set(client.Database, request.GetKey(1), request.GetValue(2));
            return RedisResult.OK;
        }
        protected virtual void Set(int database, RedisKey key, RedisValue value) => throw new NotSupportedException();
        [RedisCommand(1)]
        protected new virtual RedisResult Shutdown(RedisClient client, RedisRequest request)
        {
            DoShutdown();
            return RedisResult.OK;
        }
        [RedisCommand(2)]
        protected virtual RedisResult Strlen(RedisClient client, RedisRequest request)
            => RedisResult.Create(Strlen(client.Database, request.GetKey(1)), ResultType.Integer);

        protected virtual long Strlen(int database, RedisKey key) => Get(database, key).Length();

        [RedisCommand(-2)]
        protected virtual RedisResult Del(RedisClient client, RedisRequest request)
        {
            int count = 0;
            for (int i = 1; i < request.Count; i++)
            {
                if (Del(client.Database, request.GetKey(i)))
                    count++;
            }
            return RedisResult.Create(count, ResultType.Integer);
        }
        protected virtual bool Del(int database, RedisKey key) => throw new NotSupportedException();

        [RedisCommand(1)]
        protected virtual RedisResult Dbsize(RedisClient client, RedisRequest request)
            => RedisResult.Create(Dbsize(client.Database), ResultType.Integer);

        protected virtual long Dbsize(int database) => throw new NotSupportedException();

        [RedisCommand(1)]
        protected virtual RedisResult Flushall(RedisClient client, RedisRequest request)
        {
            var count = Databases;
            for (int i = 0; i < count; i++)
            {
                Flushdb(i);
            }
            return RedisResult.OK;
        }

        [RedisCommand(1)]
        protected virtual RedisResult Flushdb(RedisClient client, RedisRequest request)
        {
            Flushdb(client.Database);
            return RedisResult.OK;
        }
        protected virtual void Flushdb(int database) => throw new NotSupportedException();

        [RedisCommand(-1, LockFree = true, MaxArgs = 2)]
        protected virtual RedisResult Info(RedisClient client, RedisRequest request)
        {
            var info = Info(request.Count == 1 ? null : request.GetString(1));
            return RedisResult.Create(info, ResultType.BulkString);
        }
        protected virtual string Info(string selected)
        {
            var sb = new StringBuilder();
            bool IsMatch(string section) => string.IsNullOrWhiteSpace(selected)
                || string.Equals(section, selected, StringComparison.OrdinalIgnoreCase);
            if (IsMatch("Server")) Info(sb, "Server");
            if (IsMatch("Clients")) Info(sb, "Clients");
            if (IsMatch("Memory")) Info(sb, "Memory");
            if (IsMatch("Persistence")) Info(sb, "Persistence");
            if (IsMatch("Stats")) Info(sb, "Stats");
            if (IsMatch("Replication")) Info(sb, "Replication");
            if (IsMatch("Keyspace")) Info(sb, "Keyspace");
            return sb.ToString();
        }

        [RedisCommand(2)]
        protected virtual RedisResult Keys(RedisClient client, RedisRequest request)
        {
            List<RedisResult> found = null;
            foreach (var key in Keys(client.Database, request.GetKey(1)))
            {
                if (found == null) found = new List<RedisResult>();
                found.Add(RedisResult.Create(key));
            }
            return RedisResult.Create(
                found == null ? Array.Empty<RedisResult>() : found.ToArray());
        }
        protected virtual IEnumerable<RedisKey> Keys(int database, RedisKey pattern) => throw new NotSupportedException();

        protected virtual void Info(StringBuilder sb, string section)
        {
            StringBuilder AddHeader()
            {
                if (sb.Length != 0) sb.AppendLine();
                return sb.Append("# ").AppendLine(section);
            }

            switch (section)
            {
                case "Server":
                    AddHeader().AppendLine("redis_version:1.0")
                        .AppendLine("redis_mode:standalone")
                        .Append("os:").Append(Environment.OSVersion).AppendLine()
                        .Append("arch_bits:x").Append(IntPtr.Size * 8).AppendLine();
                    using (var process = Process.GetCurrentProcess())
                    {
                        sb.Append("process:").Append(process.Id).AppendLine();
                    }
                    var port = TcpPort();
                    if (port >= 0) sb.Append("tcp_port:").Append(port).AppendLine();
                    break;
                case "Clients":
                    AddHeader().Append("connected_clients:").Append(ClientCount).AppendLine();
                    break;
                case "Memory":
                    break;
                case "Persistence":
                    AddHeader().AppendLine("loading:0");
                    break;
                case "Stats":
                    AddHeader().Append("total_connections_received:").Append(TotalClientCount).AppendLine()
                        .Append("total_commands_processed:").Append(CommandsProcesed).AppendLine();
                    break;
                case "Replication":
                    AddHeader().AppendLine("role:master");
                    break;
                case "Keyspace":
                    break;
            }
        }
        [RedisCommand(2, "memory", "purge")]
        protected virtual RedisResult MemoryPurge(RedisClient client, RedisRequest request)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
            return RedisResult.OK;
        }
        [RedisCommand(-2)]
        protected virtual RedisResult Mget(RedisClient client, RedisRequest request)
        {
            int argCount = request.Count;
            var arr = new RedisResult[argCount - 1];
            var db = client.Database;
            for (int i = 1; i < argCount; i++)
            {
                arr[i - 1] = RedisResult.Create(Get(db, request.GetKey(i)), ResultType.BulkString);
            }
            return RedisResult.Create(arr);
        }
        [RedisCommand(-3)]
        protected virtual RedisResult Mset(RedisClient client, RedisRequest request)
        {
            int argCount = request.Count;
            var db = client.Database;
            for (int i = 1; i < argCount;)
            {
                Set(db, request.GetKey(i++), request.GetValue(i++));
            }
            return RedisResult.OK;
        }
        [RedisCommand(-1, LockFree = true, MaxArgs = 2)]
        protected virtual RedisResult Ping(RedisClient client, RedisRequest request)
            => RedisResult.Create(request.Count == 1 ? "PONG" : request.GetString(1), ResultType.SimpleString);

        [RedisCommand(1, LockFree = true)]
        protected virtual RedisResult Quit(RedisClient client, RedisRequest request)
        {
            RemoveClient(client);
            return RedisResult.OK;
        }

        [RedisCommand(1, LockFree = true)]
        protected virtual RedisResult Role(RedisClient client, RedisRequest request)
        {
            return RedisResult.Create(new[]
            {
                RedisResult.Create("master", ResultType.BulkString),
                RedisResult.Create(0, ResultType.Integer),
                RedisResult.Create(Array.Empty<RedisResult>())
            });
        }

        [RedisCommand(2, LockFree = true)]
        protected virtual RedisResult Select(RedisClient client, RedisRequest request)
        {
            var raw = request.GetValue(1);
            if (!raw.IsInteger) return RedisResult.Create("ERR invalid DB index", ResultType.Error);
            int db = (int)raw;
            if (db < 0 || db >= Databases) return RedisResult.Create("ERR DB index is out of range", ResultType.Error);
            client.Database = db;
            return RedisResult.OK;
        }

        [RedisCommand(-2)]
        protected virtual RedisResult Subscribe(RedisClient client, RedisRequest request)
            => SubscribeImpl(client, request);
        [RedisCommand(-2)]
        protected virtual RedisResult Unsubscribe(RedisClient client, RedisRequest request)
            => SubscribeImpl(client, request);

        private RedisResult SubscribeImpl(RedisClient client, RedisRequest request)
        {
            var reply = new RedisResult[3 * (request.Count - 1)];
            int index = 0;
            var mode = request.Command[0] == 'p' ? RedisChannel.PatternMode.Pattern : RedisChannel.PatternMode.Literal;
            for (int i = 1; i < request.Count; i++)
            {
                var channel = request.GetChannel(i, mode);
                int count;
                switch (request.Command)
                {
                    case "subscribe": count = client.Subscribe(channel); break;
                    case "unsubscribe": count = client.Unsubscribe(channel); break;
                    default: return null;
                }
                reply[index++] = RedisResult.Create(request.Command, ResultType.BulkString);
                reply[index++] = RedisResult.Create(channel);
                reply[index++] = RedisResult.Create(count, ResultType.Integer);
            }
            return RedisResult.Create(reply);
        }
        static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        [RedisCommand(1, LockFree = true)]
        protected virtual RedisResult Time(RedisClient client, RedisRequest request)
        {
            var delta = Time() - UnixEpoch;
            var ticks = delta.Ticks;
            var seconds = ticks / TimeSpan.TicksPerSecond;
            var micros = (ticks % TimeSpan.TicksPerSecond) / (TimeSpan.TicksPerMillisecond / 1000);
            return RedisResult.Create(new[] {
                RedisResult.Create(seconds, ResultType.BulkString),
                RedisResult.Create(micros, ResultType.BulkString),
            });
        }
        protected virtual DateTime Time() => DateTime.UtcNow;

        [RedisCommand(-2)]
        protected virtual RedisResult Unlink(RedisClient client, RedisRequest request)
            => Del(client, request);

        [RedisCommand(2)]
        protected virtual RedisResult Incr(RedisClient client, RedisRequest request)
            => RedisResult.Create(IncrBy(client.Database, request.GetKey(1), 1), ResultType.Integer);
        [RedisCommand(2)]
        protected virtual RedisResult Decr(RedisClient client, RedisRequest request)
    => RedisResult.Create(IncrBy(client.Database, request.GetKey(1), -1), ResultType.Integer);

        [RedisCommand(3)]
        protected virtual RedisResult IncrBy(RedisClient client, RedisRequest request)
            => RedisResult.Create(IncrBy(client.Database, request.GetKey(1), request.GetInt64(2)), ResultType.Integer);

        protected virtual long IncrBy(int database, RedisKey key, long delta)
        {
            var value = ((long)Get(database, key)) + delta;
            Set(database, key, value);
            return value;
        }

    }
}
