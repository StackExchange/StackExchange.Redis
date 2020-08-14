using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
            [CLSCompliant(false)]
            public ulong ReplicationOffset { get; }

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
                public string Port { get; }

                /// <summary>
                /// The last replication offset acked by this replica node.
                /// </summary>
                [CLSCompliant(false)]
                public ulong ReplicationOffset { get; }

                internal Replica(string ip, string port, ulong replicationOffset)
                {
                    Ip = ip;
                    Port = port;
                    ReplicationOffset = replicationOffset;
                }
            }

            internal Master(ulong replicationOffset, ICollection<Replica> replicas) : base(RedisLiterals.master)
            {
                ReplicationOffset = replicationOffset;
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
            public string MasterPort { get; }

            /// <summary>
            /// This replica's replication state.
            /// </summary>
            public ReplicationState State { get; }

            /// <summary>
            /// The last replication offset received by this replica.
            /// </summary>
            [CLSCompliant(false)]
            public ulong ReceivedReplicationOffset { get; }

            /// <summary>
            /// The state of a replica node.
            /// </summary>
            public enum ReplicationState
            {
                /// <summary>
                /// Not connected to the master node.
                /// </summary>
                NotConnected,

                /// <summary>
                /// Attempting to connect to the master node.
                /// </summary>
                Connecting,

                /// <summary>
                /// Connected to the master node and syncing commands to catch up.
                /// </summary>
                Syncing,

                /// <summary>
                /// Connected to the master node and up-to-date.
                /// </summary>
                Connected,
            }

            internal Replica() : base(RedisLiterals.slave)
            {
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

            internal Sentinel() : base(RedisLiterals.sentinel)
            {
            }
        }
    }
}
