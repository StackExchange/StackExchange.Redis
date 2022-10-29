using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net;

namespace StackExchange.Redis
{
    /// <summary>
    /// Represents the state of an individual client connection to redis.
    /// </summary>
    public sealed class ClientInfo
    {
        internal static readonly ResultProcessor<ClientInfo[]> Processor = new ClientInfoProcessor();

        /// <summary>
        /// Address (host and port) of the client.
        /// </summary>
        public EndPoint? Address { get; private set; }

        /// <summary>
        /// Total duration of the connection in seconds.
        /// </summary>
        public int AgeSeconds { get; private set; }

        /// <summary>
        /// Current database ID.
        /// </summary>
        public int Database { get; private set; }

        /// <summary>
        /// The flags associated with this connection.
        /// </summary>
        public ClientFlags Flags { get; private set; }

        /// <summary>
        /// The client flags can be a combination of:
        /// <list type="table">
        ///     <item>
        ///         <term>A</term>
        ///         <description>Connection to be closed ASAP.</description>
        ///     </item>
        ///     <item>
        ///         <term>b</term>
        ///         <description>The client is waiting in a blocking operation.</description>
        ///     </item>
        ///     <item>
        ///         <term>c</term>
        ///         <description>Connection to be closed after writing entire reply.</description>
        ///     </item>
        ///     <item>
        ///         <term>d</term>
        ///         <description>A watched keys has been modified - EXEC will fail.</description>
        ///     </item>
        ///     <item>
        ///         <term>i</term>
        ///         <description>The client is waiting for a VM I/O (deprecated).</description>
        ///     </item>
        ///     <item>
        ///         <term>M</term>
        ///         <description>The client is a primary.</description>
        ///     </item>
        ///     <item>
        ///         <term>N</term>
        ///         <description>No specific flag set.</description>
        ///     </item>
        ///     <item>
        ///         <term>O</term>
        ///         <description>The client is a replica in MONITOR mode.</description>
        ///     </item>
        ///     <item>
        ///         <term>P</term>
        ///         <description>The client is a Pub/Sub subscriber.</description>
        ///     </item>
        ///     <item>
        ///         <term>r</term>
        ///         <description>The client is in readonly mode against a cluster node.</description>
        ///     </item>
        ///     <item>
        ///         <term>S</term>
        ///         <description>The client is a normal replica server.</description>
        ///     </item>
        ///     <item>
        ///         <term>u</term>
        ///         <description>The client is unblocked.</description>
        ///     </item>
        ///     <item>
        ///         <term>U</term>
        ///         <description>The client is unblocked.</description>
        ///     </item>
        ///     <item>
        ///         <term>x</term>
        ///         <description>The client is in a MULTI/EXEC context.</description>
        ///     </item>
        ///     <item>
        ///         <term>t</term>
        ///         <description>The client enabled keys tracking in order to perform client side caching.</description>
        ///     </item>
        ///     <item>
        ///         <term>R</term>
        ///         <description>The client tracking target client is invalid.</description>
        ///     </item>
        ///     <item>
        ///         <term>B</term>
        ///         <description>The client enabled broadcast tracking mode.</description>
        ///     </item>
        /// </list>
        /// </summary>
        /// <remarks><seealso href="https://redis.io/commands/client-list"/></remarks>
        public string? FlagsRaw { get; private set; }

        /// <summary>
        /// The host of the client (typically an IP address).
        /// </summary>
        public string? Host => Format.TryGetHostPort(Address, out string? host, out _) ? host : null;

        /// <summary>
        /// Idle time of the connection in seconds.
        /// </summary>
        public int IdleSeconds { get; private set; }

        /// <summary>
        /// Last command played.
        /// </summary>
        public string? LastCommand { get; private set; }

        /// <summary>
        /// The name allocated to this connection, if any.
        /// </summary>
        public string? Name { get; private set; }

        /// <summary>
        /// Number of pattern matching subscriptions.
        /// </summary>
        public int PatternSubscriptionCount { get; private set; }

        /// <summary>
        /// The port of the client.
        /// </summary>
        public int Port => Format.TryGetHostPort(Address, out _, out int? port) ? port.Value : 0;

        /// <summary>
        /// The raw content from redis.
        /// </summary>
        public string? Raw { get; private set; }

        /// <summary>
        /// Number of channel subscriptions.
        /// </summary>
        public int SubscriptionCount { get; private set; }

        /// <summary>
        /// Number of commands in a MULTI/EXEC context.
        /// </summary>
        public int TransactionCommandLength { get; private set; }

        /// <summary>
        /// A unique 64-bit client ID (introduced in Redis 2.8.12).
        /// </summary>
        public long Id { get;private set; }

        /// <summary>
        /// Format the object as a string.
        /// </summary>
        public override string ToString()
        {
            string addr = Format.ToString(Address);
            return string.IsNullOrWhiteSpace(Name) ? addr : (addr + " - " + Name);
        }

        /// <summary>
        /// The class of the connection.
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

        internal static bool TryParse(string? input, [NotNullWhen(true)] out ClientInfo[]? clientList)
        {
            if (input == null)
            {
                clientList = null;
                return false;
            }

            var clients = new List<ClientInfo>();
            using (var reader = new StringReader(input))
            {
                while (reader.ReadLine() is string line)
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
                            case "addr" when Format.TryParseEndPoint(value, out var addr): client.Address = addr; break;
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

                                AddFlag(ref flags, value, ClientFlags.KeysTracking, 't');
                                AddFlag(ref flags, value, ClientFlags.TrackingTargetInvalid, 'R');
                                AddFlag(ref flags, value, ClientFlags.BroadcastTracking, 'B');

                                client.Flags = flags;
                                break;
                            case "id": client.Id = Format.ParseInt64(value); break;
                        }
                    }
                    clients.Add(client);
                }
            }

            clientList = clients.ToArray();
            return true;
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
                        if (TryParse(raw, out var clients))
                        {
                            SetResult(message, clients);
                            return true;
                        }
                        break;
                }
                return false;
            }
        }
    }
}
