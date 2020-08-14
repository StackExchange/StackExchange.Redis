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
    public abstract class ServerRole
    {
        /// <summary>
        /// Result of the ROLE command for a master node.
        /// </summary>
        /// <remarks>https://redis.io/commands/role#master-output</remarks>
        public sealed class Master : ServerRole
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
            }
        }

        /// <summary>
        /// Result of the ROLE command for a replica node.
        /// </summary>
        /// <remarks>https://redis.io/commands/role#output-of-the-command-on-replicas</remarks>
        public sealed class Replica : ServerRole
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
            /// This replica's replication state as known by the master node.
            /// </summary>
            public ReplicationState State { get; }

            /// <summary>
            /// Data received from the master node relative to the master's replication offset.
            /// </summary>
            public long Received { get; }

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
        }

        /// <summary>
        /// Result of the ROLE command for a sentinel node.
        /// </summary>
        /// <remarks>https://redis.io/commands/role#sentinel-output</remarks>
        public sealed class Sentinel : ServerRole
        {
            /// <summary>
            /// Master names monitored by this sentinel node.
            /// </summary>
            public ICollection<string> MonitoredMasters { get; }
        }

        private ServerRole() { } // prevent other derived types
    }
}
