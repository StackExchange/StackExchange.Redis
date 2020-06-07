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
            Databases = databases;
            var config = ServerConfiguration;
            config["timeout"] = "0";
            config["slave-read-only"] = "yes";
            config["replica-read-only"] = "yes";
            config["databases"] = databases.ToString();
            config["slaveof"] = "";
            config["replicaof"] = "";
        }
        protected override void AppendStats(StringBuilder sb)
        {
            base.AppendStats(sb);
            sb.Append("Databases: ").Append(Databases).AppendLine();
            lock (ServerSyncLock)
            {
                for (int i = 0; i < Databases; i++)
                {
                    try
                    {
                        sb.Append("Database ").Append(i).Append(": ").Append(Dbsize(i)).AppendLine(" keys");
                    }
                    catch { }
                }
            }
        }
        public int Databases { get; }

        [RedisCommand(-3)]
        protected virtual TypedRedisValue Sadd(RedisClient client, RedisRequest request)
        {
            int added = 0;
            var key = request.GetKey(1);
            for (int i = 2; i < request.Count; i++)
            {
                if (Sadd(client.Database, key, request.GetValue(i)))
                    added++;
            }
            return TypedRedisValue.Integer(added);
        }
        protected virtual bool Sadd(int database, RedisKey key, RedisValue value) => throw new NotSupportedException();

        [RedisCommand(-3)]
        protected virtual TypedRedisValue Srem(RedisClient client, RedisRequest request)
        {
            int removed = 0;
            var key = request.GetKey(1);
            for (int i = 2; i < request.Count; i++)
            {
                if (Srem(client.Database, key, request.GetValue(i)))
                    removed++;
            }
            return TypedRedisValue.Integer(removed);
        }
        protected virtual bool Srem(int database, RedisKey key, RedisValue value) => throw new NotSupportedException();

        [RedisCommand(2)]
        protected virtual TypedRedisValue Spop(RedisClient client, RedisRequest request)
            => TypedRedisValue.BulkString(Spop(client.Database, request.GetKey(1)));

        protected virtual RedisValue Spop(int database, RedisKey key) => throw new NotSupportedException();

        [RedisCommand(2)]
        protected virtual TypedRedisValue Scard(RedisClient client, RedisRequest request)
            => TypedRedisValue.Integer(Scard(client.Database, request.GetKey(1)));

        protected virtual long Scard(int database, RedisKey key) => throw new NotSupportedException();

        [RedisCommand(3)]
        protected virtual TypedRedisValue Sismember(RedisClient client, RedisRequest request)
            => Sismember(client.Database, request.GetKey(1), request.GetValue(2)) ? TypedRedisValue.One : TypedRedisValue.Zero;

        protected virtual bool Sismember(int database, RedisKey key, RedisValue value) => throw new NotSupportedException();

        [RedisCommand(3, "client", "setname", LockFree = true)]
        protected virtual TypedRedisValue ClientSetname(RedisClient client, RedisRequest request)
        {
            client.Name = request.GetString(2);
            return TypedRedisValue.OK;
        }

        [RedisCommand(2, "client", "getname", LockFree = true)]
        protected virtual TypedRedisValue ClientGetname(RedisClient client, RedisRequest request)
            => TypedRedisValue.BulkString(client.Name);

        [RedisCommand(3, "client", "reply", LockFree = true)]
        protected virtual TypedRedisValue ClientReply(RedisClient client, RedisRequest request)
        {
            if (request.IsString(2, "on")) client.SkipReplies = -1; // reply to nothing
            else if (request.IsString(2, "off")) client.SkipReplies = 0; // reply to everything
            else if (request.IsString(2, "skip")) client.SkipReplies = 2; // this one, and the next one
            else return TypedRedisValue.Error("ERR syntax error");
            return TypedRedisValue.OK;
        }

        [RedisCommand(-1)]
        protected virtual TypedRedisValue Cluster(RedisClient client, RedisRequest request)
            => request.CommandNotFound();

        [RedisCommand(-3)]
        protected virtual TypedRedisValue Lpush(RedisClient client, RedisRequest request)
        {
            var key = request.GetKey(1);
            long length = -1;
            for (int i = 2; i < request.Count; i++)
            {
                length = Lpush(client.Database, key, request.GetValue(i));
            }
            return TypedRedisValue.Integer(length);
        }

        [RedisCommand(-3)]
        protected virtual TypedRedisValue Rpush(RedisClient client, RedisRequest request)
        {
            var key = request.GetKey(1);
            long length = -1;
            for (int i = 2; i < request.Count; i++)
            {
                length = Rpush(client.Database, key, request.GetValue(i));
            }
            return TypedRedisValue.Integer(length);
        }

        [RedisCommand(2)]
        protected virtual TypedRedisValue Lpop(RedisClient client, RedisRequest request)
            => TypedRedisValue.BulkString(Lpop(client.Database, request.GetKey(1)));

        [RedisCommand(2)]
        protected virtual TypedRedisValue Rpop(RedisClient client, RedisRequest request)
            => TypedRedisValue.BulkString(Rpop(client.Database, request.GetKey(1)));

        [RedisCommand(2)]
        protected virtual TypedRedisValue Llen(RedisClient client, RedisRequest request)
            => TypedRedisValue.Integer(Llen(client.Database, request.GetKey(1)));

        protected virtual long Lpush(int database, RedisKey key, RedisValue value) => throw new NotSupportedException();
        protected virtual long Rpush(int database, RedisKey key, RedisValue value) => throw new NotSupportedException();
        protected virtual long Llen(int database, RedisKey key) => throw new NotSupportedException();
        protected virtual RedisValue Rpop(int database, RedisKey key) => throw new NotSupportedException();
        protected virtual RedisValue Lpop(int database, RedisKey key) => throw new NotSupportedException();

        [RedisCommand(4)]
        protected virtual TypedRedisValue LRange(RedisClient client, RedisRequest request)
        {
            var key = request.GetKey(1);
            long start = request.GetInt64(2), stop = request.GetInt64(3);

            var len = Llen(client.Database, key);
            if (len == 0) return TypedRedisValue.EmptyArray;

            if (start < 0) start = len + start;
            if (stop < 0) stop = len + stop;

            if (stop < 0 || start >= len || stop < start) return TypedRedisValue.EmptyArray;

            if (start < 0) start = 0;
            else if (start >= len) start = len - 1;

            if (stop < 0) stop = 0;
            else if (stop >= len) stop = len - 1;

            var arr = TypedRedisValue.Rent(checked((int)((stop - start) + 1)), out var span);
            LRange(client.Database, key, start, span);
            return arr;
        }
        protected virtual void LRange(int database, RedisKey key, long start, Span<TypedRedisValue> arr) => throw new NotSupportedException();

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
        protected virtual TypedRedisValue Config(RedisClient client, RedisRequest request)
        {
            var pattern = request.GetString(2);

            OnUpdateServerConfiguration();
            var config = ServerConfiguration;
            var matches = config.CountMatch(pattern);
            if (matches == 0) return TypedRedisValue.EmptyArray;

            var arr = TypedRedisValue.Rent(2 * matches, out var span);
            int index = 0;
            foreach (var pair in config.Wrapped)
            {
                if (IsMatch(pattern, pair.Key))
                {
                    span[index++] = TypedRedisValue.BulkString(pair.Key);
                    span[index++] = TypedRedisValue.BulkString(pair.Value);
                }
            }
            if (index != span.Length)
            {
                arr.Recycle(index);
                throw new InvalidOperationException("Configuration CountMatch fail");
            }
            return arr;
        }

        [RedisCommand(2, LockFree = true)]
        protected virtual TypedRedisValue Echo(RedisClient client, RedisRequest request)
            => TypedRedisValue.BulkString(request.GetValue(1));

        [RedisCommand(2)]
        protected virtual TypedRedisValue Exists(RedisClient client, RedisRequest request)
        {
            int count = 0;
            var db = client.Database;
            for (int i = 1; i < request.Count; i++)
            {
                if (Exists(db, request.GetKey(i)))
                    count++;
            }
            return TypedRedisValue.Integer(count);
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
        protected virtual TypedRedisValue Get(RedisClient client, RedisRequest request)
            => TypedRedisValue.BulkString(Get(client.Database, request.GetKey(1)));

        protected virtual RedisValue Get(int database, RedisKey key) => throw new NotSupportedException();

        [RedisCommand(3)]
        protected virtual TypedRedisValue Set(RedisClient client, RedisRequest request)
        {
            Set(client.Database, request.GetKey(1), request.GetValue(2));
            return TypedRedisValue.OK;
        }
        protected virtual void Set(int database, RedisKey key, RedisValue value) => throw new NotSupportedException();
        [RedisCommand(1)]
        protected new virtual TypedRedisValue Shutdown(RedisClient client, RedisRequest request)
        {
            DoShutdown(ShutdownReason.ClientInitiated);
            return TypedRedisValue.OK;
        }
        [RedisCommand(2)]
        protected virtual TypedRedisValue Strlen(RedisClient client, RedisRequest request)
            => TypedRedisValue.Integer(Strlen(client.Database, request.GetKey(1)));

        protected virtual long Strlen(int database, RedisKey key) => Get(database, key).Length();

        [RedisCommand(-2)]
        protected virtual TypedRedisValue Del(RedisClient client, RedisRequest request)
        {
            int count = 0;
            for (int i = 1; i < request.Count; i++)
            {
                if (Del(client.Database, request.GetKey(i)))
                    count++;
            }
            return TypedRedisValue.Integer(count);
        }
        protected virtual bool Del(int database, RedisKey key) => throw new NotSupportedException();

        [RedisCommand(1)]
        protected virtual TypedRedisValue Dbsize(RedisClient client, RedisRequest request)
            => TypedRedisValue.Integer(Dbsize(client.Database));

        protected virtual long Dbsize(int database) => throw new NotSupportedException();

        [RedisCommand(1)]
        protected virtual TypedRedisValue Flushall(RedisClient client, RedisRequest request)
        {
            var count = Databases;
            for (int i = 0; i < count; i++)
            {
                Flushdb(i);
            }
            return TypedRedisValue.OK;
        }

        [RedisCommand(1)]
        protected virtual TypedRedisValue Flushdb(RedisClient client, RedisRequest request)
        {
            Flushdb(client.Database);
            return TypedRedisValue.OK;
        }
        protected virtual void Flushdb(int database) => throw new NotSupportedException();

        [RedisCommand(-1, LockFree = true, MaxArgs = 2)]
        protected virtual TypedRedisValue Info(RedisClient client, RedisRequest request)
        {
            var info = Info(request.Count == 1 ? null : request.GetString(1));
            return TypedRedisValue.BulkString(info);
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
        protected virtual TypedRedisValue Keys(RedisClient client, RedisRequest request)
        {
            List<TypedRedisValue> found = null;
            foreach (var key in Keys(client.Database, request.GetKey(1)))
            {
                if (found == null) found = new List<TypedRedisValue>();
                found.Add(TypedRedisValue.BulkString(key.AsRedisValue()));
            }
            if (found == null) return TypedRedisValue.EmptyArray;
            return TypedRedisValue.MultiBulk(found);
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
                    //var port = TcpPort();
                    //if (port >= 0) sb.Append("tcp_port:").Append(port).AppendLine();
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
                        .Append("total_commands_processed:").Append(TotalCommandsProcesed).AppendLine();
                    break;
                case "Replication":
                    AddHeader().AppendLine("role:master");
                    break;
                case "Keyspace":
                    break;
            }
        }
        [RedisCommand(2, "memory", "purge")]
        protected virtual TypedRedisValue MemoryPurge(RedisClient client, RedisRequest request)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
            return TypedRedisValue.OK;
        }
        [RedisCommand(-2)]
        protected virtual TypedRedisValue Mget(RedisClient client, RedisRequest request)
        {
            int argCount = request.Count;
            var arr = TypedRedisValue.Rent(argCount - 1, out var span);
            var db = client.Database;
            for (int i = 1; i < argCount; i++)
            {
                span[i - 1] = TypedRedisValue.BulkString(Get(db, request.GetKey(i)));
            }
            return arr;
        }
        [RedisCommand(-3)]
        protected virtual TypedRedisValue Mset(RedisClient client, RedisRequest request)
        {
            int argCount = request.Count;
            var db = client.Database;
            for (int i = 1; i < argCount;)
            {
                Set(db, request.GetKey(i++), request.GetValue(i++));
            }
            return TypedRedisValue.OK;
        }
        [RedisCommand(-1, LockFree = true, MaxArgs = 2)]
        protected virtual TypedRedisValue Ping(RedisClient client, RedisRequest request)
            => TypedRedisValue.SimpleString(request.Count == 1 ? "PONG" : request.GetString(1));

        [RedisCommand(1, LockFree = true)]
        protected virtual TypedRedisValue Quit(RedisClient client, RedisRequest request)
        {
            RemoveClient(client);
            return TypedRedisValue.OK;
        }

        [RedisCommand(1, LockFree = true)]
        protected virtual TypedRedisValue Role(RedisClient client, RedisRequest request)
        {
            var arr = TypedRedisValue.Rent(3, out var span);
            span[0] = TypedRedisValue.BulkString("master");
            span[1] = TypedRedisValue.Integer(0);
            span[2] = TypedRedisValue.EmptyArray;
            return arr;
        }

        [RedisCommand(2, LockFree = true)]
        protected virtual TypedRedisValue Select(RedisClient client, RedisRequest request)
        {
            var raw = request.GetValue(1);
            if (!raw.IsInteger) return TypedRedisValue.Error("ERR invalid DB index");
            int db = (int)raw;
            if (db < 0 || db >= Databases) return TypedRedisValue.Error("ERR DB index is out of range");
            client.Database = db;
            return TypedRedisValue.OK;
        }

        [RedisCommand(-2)]
        protected virtual TypedRedisValue Subscribe(RedisClient client, RedisRequest request)
            => SubscribeImpl(client, request);
        [RedisCommand(-2)]
        protected virtual TypedRedisValue Unsubscribe(RedisClient client, RedisRequest request)
            => SubscribeImpl(client, request);

        private TypedRedisValue SubscribeImpl(RedisClient client, RedisRequest request)
        {
            var reply = TypedRedisValue.Rent(3 * (request.Count - 1), out var span);
            int index = 0;
            request.TryGetCommandBytes(0, out var cmd);
            var cmdString = TypedRedisValue.BulkString(cmd.ToArray());
            var mode = cmd[0] == (byte)'p' ? RedisChannel.PatternMode.Pattern : RedisChannel.PatternMode.Literal;
            for (int i = 1; i < request.Count; i++)
            {
                var channel = request.GetChannel(i, mode);
                int count;
                if (s_Subscribe.Equals(cmd))
                {
                    count = client.Subscribe(channel);
                }
                else if (s_Unsubscribe.Equals(cmd))
                {
                    count = client.Unsubscribe(channel);
                }
                else
                {
                    reply.Recycle(index);
                    return TypedRedisValue.Nil;
                }
                span[index++] = cmdString;
                span[index++] = TypedRedisValue.BulkString((byte[])channel);
                span[index++] = TypedRedisValue.Integer(count);
            }
            return reply;
        }
        private static readonly CommandBytes
            s_Subscribe = new CommandBytes("subscribe"),
            s_Unsubscribe = new CommandBytes("unsubscribe");
        private static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        [RedisCommand(1, LockFree = true)]
        protected virtual TypedRedisValue Time(RedisClient client, RedisRequest request)
        {
            var delta = Time() - UnixEpoch;
            var ticks = delta.Ticks;
            var seconds = ticks / TimeSpan.TicksPerSecond;
            var micros = (ticks % TimeSpan.TicksPerSecond) / (TimeSpan.TicksPerMillisecond / 1000);
            var reply = TypedRedisValue.Rent(2, out var span);
            span[0] = TypedRedisValue.BulkString(seconds);
            span[1] = TypedRedisValue.BulkString(micros);
            return reply;
        }
        protected virtual DateTime Time() => DateTime.UtcNow;

        [RedisCommand(-2)]
        protected virtual TypedRedisValue Unlink(RedisClient client, RedisRequest request)
            => Del(client, request);

        [RedisCommand(2)]
        protected virtual TypedRedisValue Incr(RedisClient client, RedisRequest request)
            => TypedRedisValue.Integer(IncrBy(client.Database, request.GetKey(1), 1));
        [RedisCommand(2)]
        protected virtual TypedRedisValue Decr(RedisClient client, RedisRequest request)
            => TypedRedisValue.Integer(IncrBy(client.Database, request.GetKey(1), -1));

        [RedisCommand(3)]
        protected virtual TypedRedisValue IncrBy(RedisClient client, RedisRequest request)
            => TypedRedisValue.Integer(IncrBy(client.Database, request.GetKey(1), request.GetInt64(2)));

        protected virtual long IncrBy(int database, RedisKey key, long delta)
        {
            var value = ((long)Get(database, key)) + delta;
            Set(database, key, value);
            return value;
        }
    }
}
