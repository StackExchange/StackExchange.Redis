using System.Collections.Generic;
using System.IO;
using System.Net;

namespace StackExchange.Redis
{
    /// <summary>
    /// Represents the state of an individual client connection to redis
    /// </summary>
    public sealed class ClientInfo
    {
        internal static readonly ResultProcessor<ClientInfo[]> Processor = new ClientInfoProcessor();

        /// <summary>
        /// Address (host and port) of the client
        /// </summary>
        public EndPoint Address { get; private set; }

        /// <summary>
        /// total duration of the connection in seconds
        /// </summary>
        public int AgeSeconds { get; private set; }

        /// <summary>
        /// current database ID
        /// </summary>
        public int Database { get; private set; }

        /// <summary>
        /// The flags associated with this connection
        /// </summary>
        public ClientFlags Flags { get; private set; }

        /// <summary>
        /// The client flags can be a combination of:
        ///
        /// A: connection to be closed ASAP
        /// b: the client is waiting in a blocking operation
        /// c: connection to be closed after writing entire reply
        /// d: a watched keys has been modified - EXEC will fail
        /// i: the client is waiting for a VM I/O (deprecated)
        /// M: the client is a master
        /// N: no specific flag set
        /// O: the client is a replica in MONITOR mode
        /// P: the client is a Pub/Sub subscriber
        /// r: the client is in readonly mode against a cluster node
        /// S: the client is a normal replica server
        /// U: the client is connected via a Unix domain socket
        /// x: the client is in a MULTI/EXEC context
        /// </summary>
        public string FlagsRaw { get; private set; }

        /// <summary>
        /// The host of the client (typically an IP address)
        /// </summary>
        public string Host => Format.TryGetHostPort(Address, out string host, out _) ? host : null;

        /// <summary>
        /// idle time of the connection in seconds
        /// </summary>
        public int IdleSeconds { get; private set; }

        /// <summary>
        ///  last command played
        /// </summary>
        public string LastCommand { get; private set; }

        /// <summary>
        /// The name allocated to this connection, if any
        /// </summary>
        public string Name { get; private set; }

        /// <summary>
        /// number of pattern matching subscriptions
        /// </summary>
        public int PatternSubscriptionCount { get; private set; }

        /// <summary>
        /// The port of the client
        /// </summary>
        public int Port => Format.TryGetHostPort(Address, out _, out int port) ? port : 0;

        /// <summary>
        /// The raw content from redis
        /// </summary>
        public string Raw { get; private set; }

        /// <summary>
        /// number of channel subscriptions
        /// </summary>
        public int SubscriptionCount { get; private set; }

        /// <summary>
        /// number of commands in a MULTI/EXEC context
        /// </summary>
        public int TransactionCommandLength { get; private set; }

        /// <summary>
        /// an unique 64-bit client ID (introduced in Redis 2.8.12).
        /// </summary>
        public long Id { get;private set; }

        /// <summary>
        /// Format the object as a string
        /// </summary>
        public override string ToString()
        {
            string addr = Format.ToString(Address);
            return string.IsNullOrWhiteSpace(Name) ? addr : (addr + " - " + Name);
        }

        /// <summary>
        /// The class of the connection
        /// </summary>
        public ClientType ClientType
        {
            get
            {
                if (SubscriptionCount != 0 || PatternSubscriptionCount != 0) return ClientType.PubSub;
                if ((Flags & ClientFlags.Replica) != 0) return ClientType.Replica;
                return ClientType.Normal;
            }
        }

        internal static ClientInfo[] Parse(string input)
        {
            if (input == null) return null;

            var clients = new List<ClientInfo>();
            using (var reader = new StringReader(input))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    var client = new ClientInfo
                    {
                        Raw = line
                    };
                    string[] tokens = line.Split(StringSplits.Space);
                    for (int i = 0; i < tokens.Length; i++)
                    {
                        string tok = tokens[i];
                        int idx = tok.IndexOf('=');
                        if (idx < 0) continue;
                        string key = tok.Substring(0, idx), value = tok.Substring(idx + 1);

                        switch (key)
                        {
                            case "addr": client.Address = Format.TryParseEndPoint(value); break;
                            case "age": client.AgeSeconds = Format.ParseInt32(value); break;
                            case "idle": client.IdleSeconds = Format.ParseInt32(value); break;
                            case "db": client.Database = Format.ParseInt32(value); break;
                            case "name": client.Name = value; break;
                            case "sub": client.SubscriptionCount = Format.ParseInt32(value); break;
                            case "psub": client.PatternSubscriptionCount = Format.ParseInt32(value); break;
                            case "multi": client.TransactionCommandLength = Format.ParseInt32(value); break;
                            case "cmd": client.LastCommand = value; break;
                            case "flags":
                                client.FlagsRaw = value;
                                ClientFlags flags = ClientFlags.None;
                                AddFlag(ref flags, value, ClientFlags.CloseASAP, 'A');
                                AddFlag(ref flags, value, ClientFlags.Blocked, 'b');
                                AddFlag(ref flags, value, ClientFlags.Closing, 'c');
                                AddFlag(ref flags, value, ClientFlags.TransactionDoomed, 'd');
                                // i: deprecated
                                AddFlag(ref flags, value, ClientFlags.Master, 'M');
                                // N: not needed
                                AddFlag(ref flags, value, ClientFlags.ReplicaMonitor, 'O');
                                AddFlag(ref flags, value, ClientFlags.PubSubSubscriber, 'P');
                                AddFlag(ref flags, value, ClientFlags.ReadOnlyCluster, 'r');
                                AddFlag(ref flags, value, ClientFlags.Replica, 'S');
                                AddFlag(ref flags, value, ClientFlags.Unblocked, 'u');
                                AddFlag(ref flags, value, ClientFlags.UnixDomainSocket, 'U');
                                AddFlag(ref flags, value, ClientFlags.Transaction, 'x');
                                
                                client.Flags = flags;
                                break;
                            case "id": client.Id = Format.ParseInt64(value); break;
                        }
                    }
                    clients.Add(client);
                }
            }

            return clients.ToArray();
        }

        private static void AddFlag(ref ClientFlags value, string raw, ClientFlags toAdd, char token)
        {
            if (raw.IndexOf(token) >= 0) value |= toAdd;
        }

        private class ClientInfoProcessor : ResultProcessor<ClientInfo[]>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                switch(result.Type)
                {
                    case ResultType.BulkString:

                        var raw = result.GetString();
                        var clients = Parse(raw);
                        SetResult(message, clients);
                        return true;
                }
                return false;
            }
        }
    }
}
