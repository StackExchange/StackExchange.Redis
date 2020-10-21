using System.Collections.Generic;

namespace StackExchange.Redis
{
    /// <summary>
    /// Result of the ROLE command. Values depend on the role: master, replica, or sentinel.
    /// </summary>
    /// <remarks>https://redis.io/commands/role</remarks>
    public abstract class Role
    {
        /// <summary>
        /// One of "master", "slave" (aka replica), or "sentinel".
        /// </summary>
        public string Value { get; }

        /// <inheritdoc/>
        public override string ToString() => Value;

        private Role(string role) => Value = role;

        /// <summary>
        /// Result of the ROLE command for a master node.
        /// </summary>
        /// <remarks>https://redis.io/commands/role#master-output</remarks>
        public sealed class Master : Role
        {
            /// <summary>
            /// The replication offset. To be consumed by replica nodes.
            /// </summary>
            public long ReplicationOffset { get; }

            /// <summary>
            /// Connected replica nodes.
            /// </summary>
            public ICollection<Replica> Replicas { get; }

            /// <summary>
            /// A connected replica node.
            /// </summary>
            public new readonly struct Replica
            {
                /// <summary>
                /// The IP address of this replica node.
                /// </summary>
                public string Ip { get; }

                /// <summary>
                /// The port number of this replica node.
                /// </summary>
                public int Port { get; }

                /// <summary>
                /// The last replication offset acked by this replica node.
                /// </summary>
                public long ReplicationOffset { get; }

                internal Replica(string ip, int port, long offset)
                {
                    Ip = ip;
                    Port = port;
                    ReplicationOffset = offset;
                }
            }

            internal Master(long offset, ICollection<Replica> replicas) : base(RedisLiterals.master)
            {
                ReplicationOffset = offset;
                Replicas = replicas;
            }
        }

        /// <summary>
        /// Result of the ROLE command for a replica node.
        /// </summary>
        /// <remarks>https://redis.io/commands/role#output-of-the-command-on-replicas</remarks>
        public sealed class Replica : Role
        {
            /// <summary>
            /// The IP address of the master node for this replica.
            /// </summary>
            public string MasterIp { get; }

            /// <summary>
            /// The port number of the master node for this replica.
            /// </summary>
            public int MasterPort { get; }

            /// <summary>
            /// This replica's replication state.
            /// </summary>
            public string State { get; }

            /// <summary>
            /// The last replication offset received by this replica.
            /// </summary>
            public long ReplicationOffset { get; }

            internal Replica(string role, string ip, int port, string state, long offset) : base(role)
            {
                MasterIp = ip;
                MasterPort = port;
                State = state;
                ReplicationOffset = offset;
            }
        }

        /// <summary>
        /// Result of the ROLE command for a sentinel node.
        /// </summary>
        /// <remarks>https://redis.io/commands/role#sentinel-output</remarks>
        public sealed class Sentinel : Role
        {
            /// <summary>
            /// Master names monitored by this sentinel node.
            /// </summary>
            public ICollection<string> MonitoredMasters { get; }

            internal Sentinel(ICollection<string> masters) : base(RedisLiterals.sentinel)
            {
                MonitoredMasters = masters;
            }
        }

        /// <summary>
        /// An unexpected result of the ROLE command.
        /// </summary>
        public sealed class Unknown : Role
        {
            internal Unknown(string role) : base(role) { }
        }
    }
}
