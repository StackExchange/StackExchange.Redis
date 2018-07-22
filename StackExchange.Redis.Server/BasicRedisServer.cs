using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace StackExchange.Redis.Server
{
    public abstract class BasicRedisServer : RedisServer
    {
        protected BasicRedisServer(int databases = 16, TextWriter output = null) : base(output)
        {
            if (databases < 1) throw new ArgumentOutOfRangeException(nameof(databases));
            var config = ServerConfiguration;
            config["timeout"] = "0";
            config["slave-read-only"] = "yes";
            config["databases"] = databases.ToString();
            config["slaveof"] = "";
        }
        public int Databases { get; }

        public override RedisResult ConnectionExecute(RedisClient client, RedisRequest request)
        {
            switch (request.Command)
            {
                case "client": return Client(client, request);
                case "echo": return Echo(client, request);
                case "ping": return Ping(client, request);
                case "quit": return Quit(client, request);
                case "select": return Select(client, request);
                default: return null;
            }
        }
        public override RedisResult ServerExecute(RedisClient client, RedisRequest request)
        {
            switch (request.Command)
            {
                case "client": return Client(client, request);
                case "cluster": return Cluster(client, request);
                case "config": return Config(client, request);
                case "dbsize": return Dbsize(client, request);
                case "del": return Del(client, request);
                case "echo": return Echo(client, request);
                case "exists": return Exists(client, request);
                case "flushall": return Flushall(client, request);
                case "flushdb": return Flushdb(client, request);
                case "get": return Get(client, request);
                case "info": return Info(client, request);
                case "mget": return Mget(client, request);
                case "mset": return Mset(client, request);
                case "ping": return Ping(client, request);
                case "quit": return Quit(client, request);
                case "select": return Select(client, request);
                case "set": return Set(client, request);
                case "shutdown": return Shutdown(client, request);
                case "subscribe": return Subscribe(client, request);
                case "unsubscribe": return Unsubscribe(client, request);
                default: return null;
            }
        }



        protected virtual RedisResult Client(RedisClient client, RedisRequest request)
        {
            var subcommand = request.GetStringLower(1);
            switch (subcommand.ToLowerInvariant())
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

            internal static bool IsMatch(string pattern, string key)
            {
                // non-trivial wildcards not implemented yet!
                return pattern == "*" || string.Equals(pattern, key, StringComparison.OrdinalIgnoreCase);
            }
        }
        protected virtual RedisResult Config(RedisClient client, RedisRequest request)
        {
            var subcommand = request.GetStringLower(1);
            switch (subcommand)
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
                    foreach (var pair in config.Wrapped)
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

        protected virtual RedisResult Exists(RedisClient client, RedisRequest request)
        {
            if (request.Count < 2) return request.WrongArgCount();

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
            => !Get(database, key).IsNull;

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
        protected new virtual RedisResult Shutdown(RedisClient client, RedisRequest request)
        {
            var chk = request.AssertCount(1, false);
            if (chk != null) return chk;

            DoShutdown();
            return RedisResult.OK;
        }
        protected virtual void Set(int database, RedisKey key, RedisValue value) => throw new NotImplementedException();

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
            for (int i = 0; i < count; i++)
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
        protected virtual RedisResult Mget(RedisClient client, RedisRequest request)
        {
            int argCount = request.Count;
            if (argCount < 2) return request.WrongArgCount();

            var arr = new RedisValue[argCount - 1];
            var db = client.Database;
            for (int i = 1; i < argCount; i++)
            {
                arr[i - 1] = Get(db, request.GetKey(i));
            }
            return RedisResult.Create(arr);
        }
        protected virtual RedisResult Mset(RedisClient client, RedisRequest request)
        {
            int argCount = request.Count;
            if (argCount < 3 || (argCount & 1) == 0) return request.WrongArgCount();

            var db = client.Database;
            for (int i = 1; i < argCount;)
            {
                Set(db, request.GetKey(i++), request[i++]);
            }
            return RedisResult.OK;
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
                switch (request.Command)
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
