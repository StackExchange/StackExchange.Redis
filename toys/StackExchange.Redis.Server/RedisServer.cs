using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;

namespace StackExchange.Redis.Server
{
    public abstract class RedisServer : RespServer
    {
        // non-trivial wildcards not implemented yet!
        public static bool IsMatch(string pattern, string key) =>
            pattern == "*" || string.Equals(pattern, key, StringComparison.OrdinalIgnoreCase);

        private ConcurrentDictionary<EndPoint, Node> _nodes = new();

        public bool TryGetNode(EndPoint endpoint, out Node node) => _nodes.TryGetValue(endpoint, out node);

        public EndPoint DefaultEndPoint
        {
            get
            {
                foreach (var pair in _nodes)
                {
                    return pair.Key;
                }
                throw new InvalidOperationException("No endpoints");
            }
        }

        public override Node DefaultNode
        {
            get
            {
                foreach (var pair in _nodes)
                {
                    return pair.Value;
                }
                return null;
            }
        }

        public IEnumerable<EndPoint> GetEndPoints()
        {
            foreach (var pair in _nodes)
            {
                yield return pair.Key;
            }
        }

        public bool Migrate(int hashSlot, EndPoint to)
        {
            if (ServerType != ServerType.Cluster) throw new InvalidOperationException($"Server mode is {ServerType}");
            if (!TryGetNode(to, out var target)) throw new KeyNotFoundException($"Target node not found: {Format.ToString(to)}");
            foreach (var pair in _nodes)
            {
                if (pair.Value.HasSlot(hashSlot))
                {
                    if (pair.Value == target) return false; // nothing to do

                    if (!pair.Value.RemoveSlot(hashSlot))
                    {
                        throw new KeyNotFoundException($"Unable to remove slot {hashSlot} from old owner");
                    }
                    target.AddSlot(hashSlot);
                    return true;
                }
            }
            throw new KeyNotFoundException($"Source node not found for slot {hashSlot}");
        }
        public bool Migrate(Span<byte> key, EndPoint to) => Migrate(ServerSelectionStrategy.GetClusterSlot(key), to);
        public bool Migrate(in RedisKey key, EndPoint to) => Migrate(GetHashSlot(key), to);

        public EndPoint AddEmptyNode()
        {
            EndPoint endpoint;
            Node node;
            do
            {
                endpoint = null;
                int maxPort = 0;
                foreach (var pair in _nodes)
                {
                    endpoint ??= pair.Key;
                    switch (pair.Key)
                    {
                        case IPEndPoint ip:
                            if (ip.Port > maxPort) maxPort = ip.Port;
                            break;
                        case DnsEndPoint dns:
                            if (dns.Port > maxPort) maxPort = dns.Port;
                            break;
                    }
                }

                switch (endpoint)
                {
                    case null:
                        endpoint = new IPEndPoint(IPAddress.Loopback, 6379);
                        break;
                    case IPEndPoint ip:
                        endpoint = new IPEndPoint(ip.Address, maxPort + 1);
                        break;
                    case DnsEndPoint dns:
                        endpoint = new DnsEndPoint(dns.Host, maxPort + 1);
                        break;
                }

                node = new(this, endpoint);
                node.UpdateSlots([]); // explicit empty range (rather than implicit "all nodes")
            }
            // defensive loop for concurrency
            while (!_nodes.TryAdd(endpoint, node));
            return endpoint;
        }

        protected RedisServer(EndPoint endpoint = null, int databases = 16, TextWriter output = null) : base(output)
        {
            endpoint ??= new IPEndPoint(IPAddress.Loopback, 6379);
            _nodes.TryAdd(endpoint, new Node(this, endpoint));
            RedisVersion = s_DefaultServerVersion;
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

        public string Password { get; set; } = "";

        public override TypedRedisValue Execute(RedisClient client, in RedisRequest request)
        {
            var pw = Password;
            if (pw.Length != 0 & !client.IsAuthenticated)
            {
                if (!Literals.IsAuthCommand(in request)) return TypedRedisValue.Error("NOAUTH Authentication required.");
            }
            return base.Execute(client, request);
        }

        internal class Literals
        {
            public static readonly CommandBytes
                AUTH = new("AUTH"u8),
                HELLO = new("HELLO"u8),
                SETNAME = new("SETNAME"u8);

            public static bool IsAuthCommand(in RedisRequest request) =>
                request.Count != 0 && request.TryGetCommandBytes(0, out var command)
                                   && (command.Equals(AUTH) || command.Equals(HELLO));
        }

        [RedisCommand(2)]
        protected virtual TypedRedisValue Auth(RedisClient client, in RedisRequest request)
        {
            if (request.GetString(1) == Password)
            {
                client.IsAuthenticated = true;
                return TypedRedisValue.OK;
            }
            return TypedRedisValue.Error("ERR invalid password");
        }

        [RedisCommand(-1)]
        protected virtual TypedRedisValue Hello(RedisClient client, in RedisRequest request)
        {
            var protocol = client.Protocol;
            bool isAuthed = client.IsAuthenticated;
            string name = client.Name;
            if (request.Count >= 2)
            {
                if (!request.TryGetInt32(1, out var protover)) return TypedRedisValue.Error("ERR Protocol version is not an integer or out of range");
                switch (protover)
                {
                    case 2:
                        protocol = RedisProtocol.Resp2;
                        break;
                    case 3: // this client does not currently support RESP3
                        protocol = RedisProtocol.Resp3;
                        break;
                    default:
                        return TypedRedisValue.Error("NOPROTO unsupported protocol version");
                }

                for (int i = 2; i < request.Count && request.TryGetCommandBytes(i, out var key); i++)
                {
                    int remaining = request.Count - (i + 1);
                    TypedRedisValue ArgFail() => TypedRedisValue.Error($"ERR Syntax error in HELLO option '{key.ToString().ToLower()}'\"");
                    if (key.Equals(Literals.AUTH))
                    {
                        if (remaining < 2) return ArgFail();
                        // ignore username for now
                        var pw = request.GetString(i + 2);
                        if (pw != Password) return TypedRedisValue.Error("WRONGPASS invalid username-password pair or user is disabled.");
                        isAuthed = true;
                        i += 2;
                    }
                    else if (key.Equals(Literals.SETNAME))
                    {
                        if (remaining < 1) return ArgFail();
                        name = request.GetString(++i);
                    }
                    else
                    {
                        return ArgFail();
                    }
                }
            }

            // all good, update client
            client.Protocol = protocol;
            client.IsAuthenticated = isAuthed;
            client.Name = name;

            var reply = TypedRedisValue.Rent(14, out var span, ResultType.Map);
            span[0] = TypedRedisValue.BulkString("server");
            span[1] = TypedRedisValue.BulkString("redis");
            span[2] = TypedRedisValue.BulkString("version");
            span[3] = TypedRedisValue.BulkString(VersionString);
            span[4] = TypedRedisValue.BulkString("proto");
            span[5] = TypedRedisValue.Integer(client.ProtocolVersion);
            span[6] = TypedRedisValue.BulkString("id");
            span[7] = TypedRedisValue.Integer(client.Id);
            span[8] = TypedRedisValue.BulkString("mode");
            span[9] = TypedRedisValue.BulkString(ModeString);
            span[10] = TypedRedisValue.BulkString("role");
            span[11] = TypedRedisValue.BulkString("master");
            span[12] = TypedRedisValue.BulkString("modules");
            span[13] = TypedRedisValue.EmptyArray(ResultType.Array);
            return reply;
        }

        [RedisCommand(-3)]
        protected virtual TypedRedisValue Sadd(RedisClient client, in RedisRequest request)
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
        protected virtual bool Sadd(int database, in RedisKey key, in RedisValue value) => throw new NotSupportedException();

        [RedisCommand(-3)]
        protected virtual TypedRedisValue Srem(RedisClient client, in RedisRequest request)
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
        protected virtual bool Srem(int database, in RedisKey key, in RedisValue value) => throw new NotSupportedException();

        [RedisCommand(2)]
        protected virtual TypedRedisValue Spop(RedisClient client, in RedisRequest request)
            => TypedRedisValue.BulkString(Spop(client.Database, request.GetKey(1)));

        protected virtual RedisValue Spop(int database, in RedisKey key) => throw new NotSupportedException();

        [RedisCommand(2)]
        protected virtual TypedRedisValue Scard(RedisClient client, in RedisRequest request)
            => TypedRedisValue.Integer(Scard(client.Database, request.GetKey(1, KeyFlags.ReadOnly)));

        protected virtual long Scard(int database, in RedisKey key) => throw new NotSupportedException();

        [RedisCommand(3)]
        protected virtual TypedRedisValue Sismember(RedisClient client, in RedisRequest request)
            => Sismember(client.Database, request.GetKey(1, KeyFlags.ReadOnly), request.GetValue(2)) ? TypedRedisValue.One : TypedRedisValue.Zero;

        protected virtual bool Sismember(int database, in RedisKey key, in RedisValue value) => throw new NotSupportedException();

        [RedisCommand(3)]
        protected virtual TypedRedisValue Rename(RedisClient client, in RedisRequest request)
        {
            RedisKey oldKey = request.GetKey(1), newKey = request.GetKey(2);
            return oldKey == newKey || Rename(client.Database, oldKey, newKey) ? TypedRedisValue.OK : TypedRedisValue.Error("ERR no such key");
        }

        protected virtual bool Rename(int database, in RedisKey oldKey, in RedisKey newKey)
        {
            // can implement with Exists/Del/Set
            if (!Exists(database, oldKey)) return false;
            Del(database, newKey);
            Set(database, newKey, Get(database, oldKey));
            Del(database, oldKey);
            return true;
        }

        [RedisCommand(4)]
        protected virtual TypedRedisValue SetEx(RedisClient client, in RedisRequest request)
        {
            RedisKey key = request.GetKey(1);
            int seconds = request.GetInt32(2);
            var value = request.GetValue(3);
            SetEx(client.Database, key, TimeSpan.FromSeconds(seconds), value);
            return TypedRedisValue.OK;
        }

        [RedisCommand(-2)]
        protected virtual TypedRedisValue Touch(RedisClient client, in RedisRequest request)
        {
            for (int i = 1; i < request.Count; i++)
            {
                Touch(client.Database, request.GetKey(i));
            }

            return TypedRedisValue.OK;
        }

        [RedisCommand(-2)]
        protected virtual TypedRedisValue Watch(RedisClient client, in RedisRequest request)
        {
            for (int i = 1; i < request.Count; i++)
            {
                var key = request.GetKey(i, KeyFlags.ReadOnly);
                if (!client.Watch(key))
                    return TypedRedisValue.Error("WATCH inside MULTI is not allowed");
            }

            return TypedRedisValue.OK;
        }

        [RedisCommand(1)]
        protected virtual TypedRedisValue Unwatch(RedisClient client, in RedisRequest request)
        {
            return client.Unwatch() ? TypedRedisValue.OK : TypedRedisValue.Error("UNWATCH inside MULTI is not allowed");
        }

        [RedisCommand(1)]
        protected virtual TypedRedisValue Multi(RedisClient client, in RedisRequest request)
        {
            return client.Multi() ? TypedRedisValue.OK : TypedRedisValue.Error("MULTI calls can not be nested");
        }

        [RedisCommand(1)]
        protected virtual TypedRedisValue Discard(RedisClient client, in RedisRequest request)
        {
            return client.Discard() ? TypedRedisValue.OK : TypedRedisValue.Error("DISCARD without MULTI");
        }

        [RedisCommand(1)]
        protected virtual TypedRedisValue Exec(RedisClient client, in RedisRequest request)
        {
            var exec = client.FlushMulti(out var commands);
            switch (exec)
            {
                case RedisClient.ExecResult.NotInTransaction:
                    return TypedRedisValue.Error("EXEC without MULTI");
                case RedisClient.ExecResult.WatchConflict:
                    return TypedRedisValue.NullArray(ResultType.Array);
                case RedisClient.ExecResult.AbortedByError:
                    return TypedRedisValue.Error("EXECABORT Transaction discarded because of previous errors.");
            }
            Debug.Assert(exec is RedisClient.ExecResult.CommandsReturned);

            var results = TypedRedisValue.Rent(commands.Length, out var span, ResultType.Array);
            int index = 0;
            foreach (var cmd in commands)
            {
                // TODO:this is the bit we can't do just yet, until we can freely parse results
                // RedisRequest inner = // ...
                // inner = inner.WithClient(client);
                // results[index++] = Execute(client, cmd);
                span[index++] = TypedRedisValue.Error($"ERR transactions not yet implemented, sorry; ignoring {Encoding.ASCII.GetString(cmd)}");
            }
            return results;
        }

        protected virtual void SetEx(int database, in RedisKey key, TimeSpan timeout, in RedisValue value)
        {
            Set(database, key, value);
            Expire(database, key, timeout);
        }

        [RedisCommand(3, "client", "setname", LockFree = true)]
        protected virtual TypedRedisValue ClientSetname(RedisClient client, in RedisRequest request)
        {
            client.Name = request.GetString(2);
            return TypedRedisValue.OK;
        }

        [RedisCommand(2, "client", "getname", LockFree = true)]
        protected virtual TypedRedisValue ClientGetname(RedisClient client, in RedisRequest request)
            => TypedRedisValue.BulkString(client.Name);

        [RedisCommand(3, "client", "reply", LockFree = true)]
        protected virtual TypedRedisValue ClientReply(RedisClient client, in RedisRequest request)
        {
            if (request.IsString(2, "on")) client.SkipReplies = -1; // reply to nothing
            else if (request.IsString(2, "off")) client.SkipReplies = 0; // reply to everything
            else if (request.IsString(2, "skip")) client.SkipReplies = 2; // this one, and the next one
            else return TypedRedisValue.Error("ERR syntax error");
            return TypedRedisValue.OK;
        }

        [RedisCommand(2, "client", "id", LockFree = true)]
        protected virtual TypedRedisValue ClientId(RedisClient client, in RedisRequest request)
            => TypedRedisValue.Integer(client.Id);

        private bool IsClusterEnabled(out TypedRedisValue fault)
        {
            if (ServerType == ServerType.Cluster)
            {
                fault = default;
                return true;
            }
            fault = TypedRedisValue.Error("ERR This instance has cluster support disabled");
            return false;
        }

        [RedisCommand(2, nameof(RedisCommand.CLUSTER), subcommand: "nodes", LockFree = true)]
        protected virtual TypedRedisValue ClusterNodes(RedisClient client, in RedisRequest request)
        {
            if (!IsClusterEnabled(out TypedRedisValue fault)) return fault;

            var sb = new StringBuilder();
            foreach (var pair in _nodes.OrderBy(x => x.Key, EndPointComparer.Instance))
            {
                var node = pair.Value;
                sb.Append(node.Id).Append(" ").Append(node.Host).Append(":").Append(node.Port).Append("@1").Append(node.Port).Append(" ");
                if (node == client.Node)
                {
                    sb.Append("myself,");
                }
                sb.Append("master - 0 0 1 connected");
                foreach (var range in node.Slots)
                {
                    sb.Append(" ").Append(range.ToString());
                }
                sb.AppendLine();
            }
            return TypedRedisValue.BulkString(sb.ToString());
        }

        [RedisCommand(2, nameof(RedisCommand.CLUSTER), subcommand: "slots", LockFree = true)]
        protected virtual TypedRedisValue ClusterSlots(RedisClient client, in RedisRequest request)
        {
            if (!IsClusterEnabled(out TypedRedisValue fault)) return fault;

            int count = 0, index = 0;
            foreach (var pair in _nodes)
            {
                count += pair.Value.Slots.Length;
            }
            var slots = TypedRedisValue.Rent(count, out var slotsSpan, ResultType.Array);
            foreach (var pair in _nodes.OrderBy(x => x.Key, EndPointComparer.Instance))
            {
                string host = GetHost(pair.Key, out int port);
                foreach (var range in pair.Value.Slots)
                {
                    if (index >= count) break; // someone changed things while we were working
                    slotsSpan[index++] = TypedRedisValue.Rent(3, out var slotSpan, ResultType.Array);
                    slotSpan[0] = TypedRedisValue.Integer(range.From);
                    slotSpan[1] = TypedRedisValue.Integer(range.To);
                    slotSpan[2] = TypedRedisValue.Rent(4, out var nodeSpan, ResultType.Array);
                    nodeSpan[0] = TypedRedisValue.BulkString(host);
                    nodeSpan[1] = TypedRedisValue.Integer(port);
                    nodeSpan[2] = TypedRedisValue.BulkString(pair.Value.Id);
                    nodeSpan[3] = TypedRedisValue.EmptyArray(ResultType.Array);
                }
            }
            return slots;
        }

        private sealed class EndPointComparer : IComparer<EndPoint>
        {
            private EndPointComparer() { }
            public static readonly EndPointComparer Instance = new();

            public int Compare(EndPoint x, EndPoint y)
            {
                if (x is null) return y is null ? 0 : -1;
                if (y is null) return 1;
                if (x is IPEndPoint ipX && y is IPEndPoint ipY)
                {
                    // ignore the address, go by port alone
                    return ipX.Port.CompareTo(ipY.Port);
                }
                if (x is DnsEndPoint dnsX && y is DnsEndPoint dnsY)
                {
                    var delta = dnsX.Host.CompareTo(dnsY.Host, StringComparison.Ordinal);
                    if (delta != 0) return delta;
                    return dnsX.Port.CompareTo(dnsY.Port);
                }

                return 0; // whatever
            }
        }

        public static string GetHost(EndPoint endpoint, out int port)
        {
            if (endpoint is IPEndPoint ip)
            {
                port = ip.Port;
                return ip.Address.ToString();
            }
            if (endpoint is DnsEndPoint dns)
            {
                port = dns.Port;
                return dns.Host;
            }
            throw new NotSupportedException("Unknown endpoint type: " + endpoint.GetType().Name);
        }

        public sealed class Node
        {
            public override string ToString()
            {
                var sb = new StringBuilder();
                sb.Append(Host).Append(":").Append(Port).Append(" (");
                var slots = _slots;
                if (slots is null)
                {
                    sb.Append("all keys");
                }
                else
                {
                    bool first = true;
                    foreach (var slot in Slots)
                    {
                        if (!first) sb.Append(",");
                        sb.Append(slot);
                        first = false;
                    }

                    if (first) sb.Append("empty");
                }
                sb.Append(")");
                return sb.ToString();
            }

            public string Host { get; }

            public int Port { get; }
            public string Id { get; } = NewId();

            private SlotRange[] _slots;

            private readonly RedisServer _server;
            public Node(RedisServer server, EndPoint endpoint)
            {
                Host = GetHost(endpoint, out var port);
                Port = port;
                _server = server;
            }

            public void UpdateSlots(SlotRange[] slots) => _slots = slots;
            public ReadOnlySpan<SlotRange> Slots => _slots ?? SlotRange.SharedAllSlots;
            public bool CheckCrossSlot => _server.CheckCrossSlot;

            public bool HasSlot(int hashSlot)
            {
                var slots = _slots;
                if (slots is null) return true; // all nodes
                foreach (var slot in slots)
                {
                    if (slot.Includes(hashSlot)) return true;
                }
                return false;
            }

            public bool HasSlot(in RedisKey key)
            {
                var slots = _slots;
                if (slots is null) return true; // all nodes
                var hashSlot = GetHashSlot(key);
                foreach (var slot in slots)
                {
                    if (slot.Includes(hashSlot)) return true;
                }
                return false;
            }

            public bool HasSlot(ReadOnlySpan<byte> key)
            {
                var slots = _slots;
                if (slots is null) return true; // all nodes
                var hashSlot = ServerSelectionStrategy.GetClusterSlot(key);
                foreach (var slot in slots)
                {
                    if (slot.Includes(hashSlot)) return true;
                }
                return false;
            }

            private static string NewId()
            {
                Span<char> data = stackalloc char[40];
#if NET
                var rand = Random.Shared;
#else
                var rand = new Random();
#endif
                ReadOnlySpan<char> alphabet = "0123456789abcdef";
                for (int i = 0; i < data.Length; i++)
                {
                    data[i] = alphabet[rand.Next(alphabet.Length)];
                }
                return data.ToString();
            }

            public void AddSlot(int hashSlot)
            {
                SlotRange[] oldSlots, newSlots;
                do
                {
                    oldSlots = _slots;
                    newSlots = oldSlots;
                    if (oldSlots is null)
                    {
                        newSlots = [new SlotRange(hashSlot, hashSlot)];
                    }
                    else
                    {
                        bool found = false;
                        int index = 0;
                        foreach (var slot in oldSlots)
                        {
                            if (slot.Includes(hashSlot)) return; // already covered
                            if (slot.To == hashSlot - 1)
                            {
                                // extend the range
                                newSlots = new SlotRange[oldSlots.Length];
                                oldSlots.AsSpan().CopyTo(newSlots);
                                newSlots[index] = new SlotRange(slot.From, hashSlot);
                                found = true;
                                break;
                            }

                            index++;
                        }

                        if (!found)
                        {
                            newSlots = [..oldSlots, new SlotRange(hashSlot, hashSlot)];
                            Array.Sort(newSlots);
                        }
                    }
                }
                while (Interlocked.CompareExchange(ref _slots, newSlots, oldSlots) != oldSlots);
            }

            public bool RemoveSlot(int hashSlot)
            {
                SlotRange[] oldSlotsRaw, newSlots;
                do
                {
                    oldSlotsRaw = _slots;
                    newSlots = oldSlotsRaw;
                    // avoid the implicit null "all slots" usage
                    var oldSlots = oldSlotsRaw ?? SlotRange.SharedAllSlots;
                    bool found = false;
                    int index = 0;
                    foreach (var s in oldSlots)
                    {
                        if (s.Includes(hashSlot))
                        {
                            found = true;
                            var oldSpan = oldSlots.AsSpan();
                            if (s.IsSingleSlot)
                            {
                                // remove it
                                newSlots = new SlotRange[oldSlots.Length - 1];
                                if (index > 0) oldSpan.Slice(0, index).CopyTo(newSlots);
                                if (index < oldSlots.Length - 1) oldSpan.Slice(index + 1).CopyTo(newSlots.AsSpan(index));
                            }
                            else if (s.From == hashSlot)
                            {
                                // truncate the start
                                newSlots = new SlotRange[oldSlots.Length];
                                oldSpan.CopyTo(newSlots);
                                newSlots[index] = new SlotRange(s.From + 1, s.To);
                            }
                            else if (s.To == hashSlot)
                            {
                                // truncate the end
                                newSlots = new SlotRange[oldSlots.Length];
                                oldSpan.CopyTo(newSlots);
                                newSlots[index] = new SlotRange(s.From, s.To - 1);
                            }
                            else
                            {
                                // split it
                                newSlots = new SlotRange[oldSlots.Length + 1];
                                if (index > 0) oldSpan.Slice(0, index).CopyTo(newSlots);
                                newSlots[index] = new SlotRange(s.From, hashSlot - 1);
                                newSlots[index + 1] = new SlotRange(hashSlot + 1, s.To);
                                if (index < oldSlots.Length - 1) oldSpan.Slice(index + 1).CopyTo(newSlots.AsSpan(index + 2));
                            }
                            break;
                        }
                        index++;
                    }

                    if (!found) return false;
                }
                while (Interlocked.CompareExchange(ref _slots, newSlots, oldSlotsRaw) != oldSlotsRaw);

                return true;
            }

            public void AssertKey(in RedisKey key)
            {
                var slots = _slots;
                if (slots is not null)
                {
                    var hashSlot = GetHashSlot(key);
                    if (!HasSlot(hashSlot)) KeyMovedException.Throw(hashSlot);
                }
            }

            public void Touch(int db, in RedisKey key) => _server.Touch(db, key);
        }

        public virtual bool CheckCrossSlot => ServerType == ServerType.Cluster;

        protected override Node GetNode(int hashSlot)
        {
            foreach (var pair in _nodes)
            {
                if (pair.Value.HasSlot(hashSlot)) return pair.Value;
            }
            return base.GetNode(hashSlot);
        }

        [RedisCommand(-1)]
        protected virtual TypedRedisValue Sentinel(RedisClient client, in RedisRequest request)
            => request.CommandNotFound();

        [RedisCommand(-3)]
        protected virtual TypedRedisValue Lpush(RedisClient client, in RedisRequest request)
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
        protected virtual TypedRedisValue Rpush(RedisClient client, in RedisRequest request)
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
        protected virtual TypedRedisValue Lpop(RedisClient client, in RedisRequest request)
            => TypedRedisValue.BulkString(Lpop(client.Database, request.GetKey(1)));

        [RedisCommand(2)]
        protected virtual TypedRedisValue Rpop(RedisClient client, in RedisRequest request)
            => TypedRedisValue.BulkString(Rpop(client.Database, request.GetKey(1)));

        [RedisCommand(2)]
        protected virtual TypedRedisValue Llen(RedisClient client, in RedisRequest request)
            => TypedRedisValue.Integer(Llen(client.Database, request.GetKey(1, KeyFlags.ReadOnly)));

        protected virtual long Lpush(int database, in RedisKey key, in RedisValue value) => throw new NotSupportedException();
        protected virtual long Rpush(int database, in RedisKey key, in RedisValue value) => throw new NotSupportedException();
        protected virtual long Llen(int database, in RedisKey key) => throw new NotSupportedException();
        protected virtual RedisValue Rpop(int database, in RedisKey key) => throw new NotSupportedException();
        protected virtual RedisValue Lpop(int database, in RedisKey key) => throw new NotSupportedException();

        [RedisCommand(4)]
        protected virtual TypedRedisValue LRange(RedisClient client, in RedisRequest request)
        {
            var key = request.GetKey(1, KeyFlags.ReadOnly);
            long start = request.GetInt64(2), stop = request.GetInt64(3);

            var len = Llen(client.Database, key);
            if (len == 0) return TypedRedisValue.EmptyArray(ResultType.Array);

            if (start < 0) start = len + start;
            if (stop < 0) stop = len + stop;

            if (stop < 0 || start >= len || stop < start) return TypedRedisValue.EmptyArray(ResultType.Array);

            if (start < 0) start = 0;
            else if (start >= len) start = len - 1;

            if (stop < 0) stop = 0;
            else if (stop >= len) stop = len - 1;

            var arr = TypedRedisValue.Rent(checked((int)((stop - start) + 1)), out var span, ResultType.Array);
            LRange(client.Database, key, start, span);
            return arr;
        }
        protected virtual void LRange(int database, in RedisKey key, long start, Span<TypedRedisValue> arr) => throw new NotSupportedException();

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
        protected virtual TypedRedisValue Config(RedisClient client, in RedisRequest request)
        {
            var pattern = request.GetString(2);

            OnUpdateServerConfiguration();
            var config = ServerConfiguration;
            var matches = config.CountMatch(pattern);
            if (matches == 0) return TypedRedisValue.EmptyArray(ResultType.Map);

            var arr = TypedRedisValue.Rent(2 * matches, out var span, ResultType.Map);
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
        protected virtual TypedRedisValue Echo(RedisClient client, in RedisRequest request)
            => TypedRedisValue.BulkString(request.GetValue(1));

        [RedisCommand(2)]
        protected virtual TypedRedisValue Exists(RedisClient client, in RedisRequest request)
        {
            int count = 0;
            var db = client.Database;
            for (int i = 1; i < request.Count; i++)
            {
                if (Exists(db, request.GetKey(i, KeyFlags.ReadOnly)))
                    count++;
            }
            return TypedRedisValue.Integer(count);
        }

        protected virtual bool Exists(int database, in RedisKey key)
        {
            try
            {
                return !Get(database, key).IsNull;
            }
            catch (InvalidCastException) { return true; } // to be an invalid cast, it must exist
        }

        [RedisCommand(2)]
        protected virtual TypedRedisValue Get(RedisClient client, in RedisRequest request)
            => TypedRedisValue.BulkString(Get(client.Database, request.GetKey(1, KeyFlags.ReadOnly)));

        protected virtual RedisValue Get(int database, in RedisKey key) => throw new NotSupportedException();

        [RedisCommand(3)]
        protected virtual TypedRedisValue Set(RedisClient client, in RedisRequest request)
        {
            Set(client.Database, request.GetKey(1), request.GetValue(2));
            return TypedRedisValue.OK;
        }
        protected virtual void Set(int database, in RedisKey key, in RedisValue value) => throw new NotSupportedException();
        [RedisCommand(1)]
        protected new virtual TypedRedisValue Shutdown(RedisClient client, in RedisRequest request)
        {
            DoShutdown(ShutdownReason.ClientInitiated);
            return TypedRedisValue.OK;
        }
        [RedisCommand(2)]
        protected virtual TypedRedisValue Strlen(RedisClient client, in RedisRequest request)
            => TypedRedisValue.Integer(Strlen(client.Database, request.GetKey(1, KeyFlags.ReadOnly)));

        protected virtual long Strlen(int database, in RedisKey key) => Get(database, key).Length();

        [RedisCommand(-2)]
        protected virtual TypedRedisValue Del(RedisClient client, in RedisRequest request)
        {
            int count = 0;
            for (int i = 1; i < request.Count; i++)
            {
                if (Del(client.Database, request.GetKey(i)))
                    count++;
            }
            return TypedRedisValue.Integer(count);
        }
        protected virtual bool Del(int database, in RedisKey key) => throw new NotSupportedException();

        [RedisCommand(2)]
        protected virtual TypedRedisValue GetDel(RedisClient client, in RedisRequest request)
        {
            var key = request.GetKey(1);
            var value = Get(client.Database, key);
            if (!value.IsNull) Del(client.Database, key);
            return TypedRedisValue.BulkString(value);
        }

        [RedisCommand(1)]
        protected virtual TypedRedisValue Dbsize(RedisClient client, in RedisRequest request)
            => TypedRedisValue.Integer(Dbsize(client.Database));

        protected virtual long Dbsize(int database) => throw new NotSupportedException();

        [RedisCommand(3)]
        protected virtual TypedRedisValue Expire(RedisClient client, in RedisRequest request)
        {
            var key = request.GetKey(1);
            var seconds = request.GetInt32(2);
            return TypedRedisValue.Integer(Expire(client.Database, key, TimeSpan.FromSeconds(seconds)) ? 1 : 0);
        }

        [RedisCommand(3)]
        protected virtual TypedRedisValue PExpire(RedisClient client, in RedisRequest request)
        {
            var key = request.GetKey(1);
            var millis = request.GetInt64(2);
            return TypedRedisValue.Integer(Expire(client.Database, key, TimeSpan.FromMilliseconds(millis)) ? 1 : 0);
        }

        [RedisCommand(2)]
        protected virtual TypedRedisValue Ttl(RedisClient client, in RedisRequest request)
        {
            var key = request.GetKey(1, KeyFlags.ReadOnly);
            var ttl = Ttl(client.Database, key);
            if (ttl == null || ttl <= TimeSpan.Zero) return TypedRedisValue.Integer(-2);
            if (ttl == TimeSpan.MaxValue) return TypedRedisValue.Integer(-1);
            return TypedRedisValue.Integer((int)ttl.Value.TotalSeconds);
        }

        protected virtual TimeSpan? Ttl(int database, in RedisKey key) => throw new NotSupportedException();

        [RedisCommand(2)]
        protected virtual TypedRedisValue Pttl(RedisClient client, in RedisRequest request)
        {
            var key = request.GetKey(1, KeyFlags.ReadOnly);
            var ttl = Ttl(client.Database, key);
            if (ttl == null || ttl <= TimeSpan.Zero) return TypedRedisValue.Integer(-2);
            if (ttl == TimeSpan.MaxValue) return TypedRedisValue.Integer(-1);
            return TypedRedisValue.Integer((long)ttl.Value.TotalMilliseconds);
        }

        protected virtual bool Expire(int database, in RedisKey key, TimeSpan timeout) => throw new NotSupportedException();

        [RedisCommand(1)]
        protected virtual TypedRedisValue Flushall(RedisClient client, in RedisRequest request)
        {
            var count = Databases;
            for (int i = 0; i < count; i++)
            {
                Flushdb(i);
            }
            return TypedRedisValue.OK;
        }

        [RedisCommand(1)]
        protected virtual TypedRedisValue Flushdb(RedisClient client, in RedisRequest request)
        {
            Flushdb(client.Database);
            return TypedRedisValue.OK;
        }
        protected virtual void Flushdb(int database) => throw new NotSupportedException();

        [RedisCommand(-1, LockFree = true, MaxArgs = 2)]
        protected virtual TypedRedisValue Info(RedisClient client, in RedisRequest request)
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
            if (IsMatch("Cluster")) Info(sb, "Cluster");
            if (IsMatch("Keyspace")) Info(sb, "Keyspace");
            return sb.ToString();
        }

        [RedisCommand(2)]
        protected virtual TypedRedisValue Keys(RedisClient client, in RedisRequest request)
        {
            List<TypedRedisValue> found = null;
            bool checkSlot = ServerType is ServerType.Cluster;
            var node = client.Node ?? DefaultNode;
            foreach (var key in Keys(client.Database, request.GetKey(1, flags: KeyFlags.NoSlotCheck | KeyFlags.ReadOnly)))
            {
                if (checkSlot && !node.HasSlot(key)) continue;
                if (found == null) found = new List<TypedRedisValue>();
                found.Add(TypedRedisValue.BulkString(key.AsRedisValue()));
            }
            if (found == null) return TypedRedisValue.EmptyArray(ResultType.Array);
            return TypedRedisValue.MultiBulk(found, ResultType.Array);
        }
        protected virtual IEnumerable<RedisKey> Keys(int database, in RedisKey pattern) => throw new NotSupportedException();

        private static readonly Version s_DefaultServerVersion = new(1, 0, 0);

        private string _versionString;
        private string VersionString => _versionString;
        private static string FormatVersion(Version v)
        {
            var sb = new StringBuilder().Append(v.Major).Append('.').Append(v.Minor);
            if (v.Revision >= 0) sb.Append('.').Append(v.Revision);
            if (v.Build >= 0) sb.Append('.').Append(v.Build);
            return sb.ToString();
        }

        public Version RedisVersion
        {
            get;
            set
            {
                if (field == value) return;
                field = value;
                _versionString = FormatVersion(value);
            }
        }

        public DateTime StartTime { get; set; } = DateTime.UtcNow;
        public ServerType ServerType { get; set; } = ServerType.Standalone;

        private string ModeString => ServerType switch
        {
            ServerType.Cluster => "cluster",
            ServerType.Sentinel => "sentinel",
            _ => "standalone",
        };
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
                    var v = RedisVersion;
                    AddHeader().Append("redis_version:").AppendLine(VersionString)
                        .Append("redis_mode:").Append(ModeString).AppendLine()
                        .Append("os:").Append(Environment.OSVersion).AppendLine()
                        .Append("arch_bits:x").Append(IntPtr.Size * 8).AppendLine();
                    using (var process = Process.GetCurrentProcess())
                    {
                        sb.Append("process_id:").Append(process.Id).AppendLine();
                    }
                    var time = DateTime.UtcNow - StartTime;
                    sb.Append("uptime_in_seconds:").Append((int)time.TotalSeconds).AppendLine();
                    sb.Append("uptime_in_days:").Append((int)time.TotalDays).AppendLine();
                    // var port = TcpPort();
                    // if (port >= 0) sb.Append("tcp_port:").Append(port).AppendLine();
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
                case "Cluster":
                    AddHeader().Append("cluster_enabled:").Append(ServerType is ServerType.Cluster ? 1 : 0).AppendLine();
                    break;
                case "Keyspace":
                    break;
            }
        }

        [RedisCommand(2, "memory", "purge")]
        protected virtual TypedRedisValue MemoryPurge(RedisClient client, in RedisRequest request)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
            return TypedRedisValue.OK;
        }
        [RedisCommand(-2)]
        protected virtual TypedRedisValue Mget(RedisClient client, in RedisRequest request)
        {
            int argCount = request.Count;
            var arr = TypedRedisValue.Rent(argCount - 1, out var span, ResultType.Map);
            var db = client.Database;
            for (int i = 1; i < argCount; i++)
            {
                span[i - 1] = TypedRedisValue.BulkString(Get(db, request.GetKey(i, KeyFlags.ReadOnly)));
            }
            return arr;
        }
        [RedisCommand(-3)]
        protected virtual TypedRedisValue Mset(RedisClient client, in RedisRequest request)
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
        protected virtual TypedRedisValue Ping(RedisClient client, in RedisRequest request)
            => TypedRedisValue.SimpleString(request.Count == 1 ? "PONG" : request.GetString(1));

        [RedisCommand(1, LockFree = true)]
        protected virtual TypedRedisValue Quit(RedisClient client, in RedisRequest request)
        {
            RemoveClient(client);
            return TypedRedisValue.OK;
        }

        [RedisCommand(1, LockFree = true)]
        protected virtual TypedRedisValue Role(RedisClient client, in RedisRequest request)
        {
            var arr = TypedRedisValue.Rent(3, out var span, ResultType.Array);
            span[0] = TypedRedisValue.BulkString("master");
            span[1] = TypedRedisValue.Integer(0);
            span[2] = TypedRedisValue.EmptyArray(ResultType.Array);
            return arr;
        }

        [RedisCommand(2, LockFree = true)]
        protected virtual TypedRedisValue Select(RedisClient client, in RedisRequest request)
        {
            var raw = request.GetValue(1);
            if (!raw.IsInteger) return TypedRedisValue.Error("ERR invalid DB index");
            int db = (int)raw;
            if (db < 0 || db >= Databases) return TypedRedisValue.Error("ERR DB index is out of range");
            client.Database = db;
            return TypedRedisValue.OK;
        }

        [RedisCommand(-2)]
        protected virtual TypedRedisValue Subscribe(RedisClient client, in RedisRequest request)
            => SubscribeImpl(client, request);
        [RedisCommand(-2)]
        protected virtual TypedRedisValue Unsubscribe(RedisClient client, in RedisRequest request)
            => SubscribeImpl(client, request);

        private TypedRedisValue SubscribeImpl(RedisClient client, in RedisRequest request)
        {
            var reply = TypedRedisValue.Rent(3 * (request.Count - 1), out var span, ResultType.Array);
            int index = 0;
            request.TryGetCommandBytes(0, out var cmd);
            var cmdString = TypedRedisValue.BulkString(cmd.ToArray());
            var mode = cmd[0] == (byte)'p' ? RedisChannel.RedisChannelOptions.Pattern : RedisChannel.RedisChannelOptions.None;
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
        protected virtual TypedRedisValue Time(RedisClient client, in RedisRequest request)
        {
            var delta = Time() - UnixEpoch;
            var ticks = delta.Ticks;
            var seconds = ticks / TimeSpan.TicksPerSecond;
            var micros = (ticks % TimeSpan.TicksPerSecond) / (TimeSpan.TicksPerMillisecond / 1000);
            var reply = TypedRedisValue.Rent(2, out var span, ResultType.Array);
            span[0] = TypedRedisValue.BulkString(seconds);
            span[1] = TypedRedisValue.BulkString(micros);
            return reply;
        }
        protected virtual DateTime Time() => DateTime.UtcNow;

        [RedisCommand(-2)]
        protected virtual TypedRedisValue Unlink(RedisClient client, in RedisRequest request)
            => Del(client, request);

        [RedisCommand(2)]
        protected virtual TypedRedisValue Incr(RedisClient client, in RedisRequest request)
            => TypedRedisValue.Integer(IncrBy(client.Database, request.GetKey(1), 1));
        [RedisCommand(2)]
        protected virtual TypedRedisValue Decr(RedisClient client, in RedisRequest request)
            => TypedRedisValue.Integer(IncrBy(client.Database, request.GetKey(1), -1));

        [RedisCommand(3)]
        protected virtual TypedRedisValue DecrBy(RedisClient client, in RedisRequest request)
            => TypedRedisValue.Integer(IncrBy(client.Database, request.GetKey(1), -request.GetInt64(2)));

        [RedisCommand(3)]
        protected virtual TypedRedisValue IncrBy(RedisClient client, in RedisRequest request)
            => TypedRedisValue.Integer(IncrBy(client.Database, request.GetKey(1), request.GetInt64(2)));

        protected virtual long IncrBy(int database, in RedisKey key, long delta)
        {
            var value = ((long)Get(database, key)) + delta;
            Set(database, key, value);
            return value;
        }
    }
}
