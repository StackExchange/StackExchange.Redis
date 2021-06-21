using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace StackExchange.Redis
{
    /// <summary>
    /// Indicates a range of slots served by a cluster node
    /// </summary>
    public readonly struct SlotRange : IEquatable<SlotRange>, IComparable<SlotRange>, IComparable
    {
        private readonly short from, to;

        /// <summary>
        /// Create a new SlotRange value
        /// </summary>
        /// <param name="from">The slot ID to start at.</param>
        /// <param name="to">The slot ID to end at.</param>
        public SlotRange(int from, int to)
        {
            checked
            {
                this.from = (short)from;
                this.to = (short)to;
            }
        }

        private SlotRange(short from, short to)
        {
            this.from = from;
            this.to = to;
        }
        /// <summary>
        /// The start of the range (inclusive)
        /// </summary>
        public int From => from;

        /// <summary>
        /// The end of the range (inclusive)
        /// </summary>
        public int To => to;

        /// <summary>
        /// Indicates whether two ranges are not equal
        /// </summary>
        /// <param name="x">The first slot range.</param>
        /// <param name="y">The second slot range.</param>
        public static bool operator !=(SlotRange x, SlotRange y) => x.from != y.from || x.to != y.to;

        /// <summary>
        /// Indicates whether two ranges are equal.
        /// </summary>
        /// <param name="x">The first slot range.</param>
        /// <param name="y">The second slot range.</param>
        public static bool operator ==(SlotRange x, SlotRange y) => x.from == y.from && x.to == y.to;

        /// <summary>
        /// Try to parse a string as a range.
        /// </summary>
        /// <param name="range">The range string to parse, e.g."1-12".</param>
        /// <param name="value">The parsed <see cref="SlotRange"/>, if successful.</param>
        public static bool TryParse(string range, out SlotRange value)
        {
            if (string.IsNullOrWhiteSpace(range))
            {
                value = default(SlotRange);
                return false;
            }
            int i = range.IndexOf('-');
            short from;
            if (i < 0)
            {
                if (TryParseInt16(range, 0, range.Length, out from))
                {
                    value = new SlotRange(from, from);
                    return true;
                }
            }
            else
            {
                if (TryParseInt16(range, 0, i++, out from) && TryParseInt16(range, i, range.Length - i, out short to))
                {
                    value = new SlotRange(from, to);
                    return true;
                }
            }
            value = default(SlotRange);
            return false;
        }

        /// <summary>
        /// Compares the current instance with another object of the same type and returns an integer that indicates whether the current instance precedes, follows, or occurs in the same position in the sort order as the other object.
        /// </summary>
        /// <param name="other">The other slot range to compare to.</param>
        public int CompareTo(SlotRange other)
        {
            int delta = (int)from - (int)other.from;
            return delta == 0 ? (int)to - (int)other.to : delta;
        }

        /// <summary>
        /// See Object.Equals
        /// </summary>
        /// <param name="obj">The other slot range to compare to.</param>
        public override bool Equals(object obj) => obj is SlotRange sRange && Equals(sRange);

        /// <summary>
        /// Indicates whether two ranges are equal
        /// </summary>
        /// <param name="other">The other slot range to compare to.</param>
        public bool Equals(SlotRange other) => other.from == from && other.to == to;

        /// <summary>
        /// See Object.GetHashCode()
        /// </summary>
        public override int GetHashCode()
        {
            int x = from, y = to; // makes CS0675 a little happier
            return x | (y << 16);
        }

        /// <summary>
        /// See Object.ToString()
        /// </summary>
        public override string ToString() => from == to ? from.ToString() : (from + "-" + to);

        internal bool Includes(int hashSlot) => hashSlot >= from && hashSlot <= to;

        private static bool TryParseInt16(string s, int offset, int count, out short value)
        {
            checked
            {
                value = 0;
                int tmp = 0;
                for (int i = 0; i < count; i++)
                {
                    char c = s[offset + i];
                    if (c < '0' || c > '9') return false;
                    tmp = (tmp * 10) + (c - '0');
                }
                value = (short)tmp;
                return true;
            }
        }

        int IComparable.CompareTo(object obj) => obj is SlotRange sRange ? CompareTo(sRange) : -1;
    }

    /// <summary>
    /// Describes the state of the cluster as reported by a single node
    /// </summary>
    public sealed class ClusterConfiguration
    {
        private readonly Dictionary<EndPoint, ClusterNode> nodeLookup = new Dictionary<EndPoint, ClusterNode>();

        private readonly ServerSelectionStrategy serverSelectionStrategy;
        internal ClusterConfiguration(ServerSelectionStrategy serverSelectionStrategy, string nodes, EndPoint origin)
        {
            // Beware: Any exception thrown here will wreak silent havoc like inability to connect to cluster nodes or non returning calls
            this.serverSelectionStrategy = serverSelectionStrategy;
            Origin = origin;
            using (var reader = new StringReader(nodes))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var node = new ClusterNode(this, line, origin);

                    // Be resilient to ":0 {master,replica},fail,noaddr" nodes, and nodes where the endpoint doesn't parse
                    if (node.IsNoAddr || node.EndPoint == null)
                        continue;

                    // Override the origin value with the endpoint advertised with the target node to
                    // make sure that things like clusterConfiguration[clusterConfiguration.Origin]
                    // will work as expected.
                    if (node.IsMyself)
                        Origin = node.EndPoint;

                    if (nodeLookup.ContainsKey(node.EndPoint))
                    {
                        // Deal with conflicting node entries for the same endpoint
                        // This can happen in dynamic environments when a node goes down and a new one is created
                        // to replace it.
                        if (!node.IsConnected)
                        {
                            // The node we're trying to add is probably about to become stale. Ignore it.
                            continue;
                        }
                        else if (!nodeLookup[node.EndPoint].IsConnected)
                        {
                            // The node we registered previously is probably stale. Replace it with a known good node.
                            nodeLookup[node.EndPoint] = node;
                        }
                        else
                        {
                            // We have conflicting connected nodes. There's nothing much we can do other than
                            // wait for the cluster state to converge and refresh on the next pass.
                            // The same is true if we have multiple disconnected nodes.
                        }
                    }
                    else
                    {
                        nodeLookup.Add(node.EndPoint, node);
                    }
                }
            }
        }

        /// <summary>
        /// Gets all nodes contained in the configuration
        /// </summary>
        /// <returns></returns>
        public ICollection<ClusterNode> Nodes => nodeLookup.Values;

        /// <summary>
        /// The node that was asked for the configuration
        /// </summary>
        public EndPoint Origin { get; }

        /// <summary>
        /// Obtain the node relating to a specified endpoint
        /// </summary>
        /// <param name="endpoint">The endpoint to get a cluster node from.</param>
        public ClusterNode this[EndPoint endpoint] => endpoint == null
            ? null
            : nodeLookup.TryGetValue(endpoint, out ClusterNode result) ? result : null;

        internal ClusterNode this[string nodeId]
        {
            get
            {
                if (string.IsNullOrWhiteSpace(nodeId)) return null;
                foreach (var pair in nodeLookup)
                {
                    if (pair.Value.NodeId == nodeId) return pair.Value;
                }
                return null;
            }
        }

        /// <summary>
        /// Gets the node that serves the specified slot.
        /// </summary>
        /// <param name="slot">The slot ID to get a node by.</param>
        public ClusterNode GetBySlot(int slot)
        {
            foreach(var node in Nodes)
            {
                if (!node.IsReplica && node.ServesSlot(slot)) return node;
            }
            return null;
        }

        /// <summary>
        /// Gets the node that serves the specified key's slot.
        /// </summary>
        /// <param name="key">The key to identify a node by.</param>
        public ClusterNode GetBySlot(RedisKey key) => GetBySlot(serverSelectionStrategy.HashSlot(key));
    }

    /// <summary>
    /// Represents the configuration of a single node in a cluster configuration.
    /// </summary>
    public sealed class ClusterNode :  IEquatable<ClusterNode>, IComparable<ClusterNode>, IComparable
    {
        private static readonly ClusterNode Dummy = new ClusterNode();

        private readonly ClusterConfiguration configuration;

        private IList<ClusterNode> children;

        private ClusterNode parent;

        private string toString;

        internal ClusterNode() { }
        internal ClusterNode(ClusterConfiguration configuration, string raw, EndPoint origin)
        {
            // https://redis.io/commands/cluster-nodes
            this.configuration = configuration;
            Raw = raw;
            var parts = raw.Split(StringSplits.Space);

            var flags = parts[2].Split(StringSplits.Comma);

            // redis 4 changes the format of "cluster nodes" - adds @... to the endpoint
            var ep = parts[1];
            int at = ep.IndexOf('@');
            if (at >= 0) ep = ep.Substring(0, at);

            EndPoint = Format.TryParseEndPoint(ep);
            if (flags.Contains("myself"))
            {
                IsMyself = true;
                if (EndPoint == null)
                {
                    // Unconfigured cluster nodes might report themselves as endpoint ":{port}",
                    // hence the origin fallback value to make sure that we can address them
                    EndPoint = origin;
                }
            }

            NodeId = parts[0];
            IsReplica = flags.Contains("slave") || flags.Contains("replica");
            IsNoAddr = flags.Contains("noaddr");
            ParentNodeId = string.IsNullOrWhiteSpace(parts[3]) ? null : parts[3];

            List<SlotRange> slots = null;

            for (int i = 8; i < parts.Length; i++)
            {
                if (SlotRange.TryParse(parts[i], out SlotRange range))
                {
                    (slots ??= new List<SlotRange>(parts.Length - i)).Add(range);
                }
            }
            Slots = slots?.AsReadOnly() ?? (IList<SlotRange>)Array.Empty<SlotRange>();
            IsConnected = parts[7] == "connected"; // Can be "connected" or "disconnected"
        }
        /// <summary>
        /// Gets all child nodes of the current node
        /// </summary>
        public IList<ClusterNode> Children
        {
            get
            {
                if (children != null) return children;

                List<ClusterNode> nodes = null;
                foreach (var node in configuration.Nodes)
                {
                    if (node.ParentNodeId == NodeId)
                    {
                        (nodes ??= new List<ClusterNode>()).Add(node);
                    }
                }
                children = nodes?.AsReadOnly() ?? (IList<ClusterNode>)Array.Empty<ClusterNode>();
                return children;
            }
        }

        /// <summary>
        /// Gets the endpoint of the current node
        /// </summary>
        public EndPoint EndPoint { get; }

        /// <summary>
        /// Gets whether this is the node which responded to the CLUSTER NODES request
        /// </summary>
        public bool IsMyself { get; }

        /// <summary>
        /// Gets whether this node is a replica
        /// </summary>
        [Obsolete("Starting with Redis version 5, Redis has moved to 'replica' terminology. Please use " + nameof(IsReplica) + " instead.")]
        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
        public bool IsSlave => IsReplica;
        /// <summary>
        /// Gets whether this node is a replica
        /// </summary>
        public bool IsReplica { get; }

        /// <summary>
        /// Gets whether this node is flagged as noaddr
        /// </summary>
        public bool IsNoAddr { get; }

        /// <summary>
        /// Gets the node's connection status
        /// </summary>
        public bool IsConnected { get; }

        /// <summary>
        /// Gets the unique node-id of the current node
        /// </summary>
        public string NodeId { get; }

        /// <summary>
        /// Gets the parent node of the current node
        /// </summary>
        public ClusterNode Parent
        {
            get
            {
                if (parent != null) return parent == Dummy ? null : parent;
                ClusterNode found = configuration[ParentNodeId];
                parent = found ?? Dummy;
                return found;
            }
        }

        /// <summary>
        /// Gets the unique node-id of the parent of the current node
        /// </summary>
        public string ParentNodeId { get; }

        /// <summary>
        /// The configuration as reported by the server
        /// </summary>
        public string Raw { get; }

        /// <summary>
        /// The slots owned by this server
        /// </summary>
        public IList<SlotRange> Slots { get; }

        /// <summary>
        /// Compares the current instance with another object of the same type and returns an integer that indicates whether the current instance precedes, follows, or occurs in the same position in the sort order as the other object.
        /// </summary>
        /// <param name="other">The <see cref="ClusterNode"/> to compare to.</param>
        public int CompareTo(ClusterNode other)
        {
            if (other == null) return -1;

            if (IsReplica != other.IsReplica) return IsReplica ? 1 : -1; // masters first

            if (IsReplica) // both replicas? compare by parent, so we get masters A, B, C and then replicas of A, B, C
            {
                int i = string.CompareOrdinal(ParentNodeId, other.ParentNodeId);
                if (i != 0) return i;
            }
            return string.CompareOrdinal(NodeId, other.NodeId);
        }

        /// <summary>
        /// See Object.Equals
        /// </summary>
        /// <param name="obj">The <see cref="ClusterNode"/> to compare to.</param>
        public override bool Equals(object obj) => Equals(obj as ClusterNode);

        /// <summary>
        /// Indicates whether two ClusterNode instances are equivalent
        /// </summary>
        /// <param name="other">The <see cref="ClusterNode"/> to compare to.</param>
        public bool Equals(ClusterNode other)
        {
            if (other == null) return false;

            return ToString() == other.ToString(); // lazy, but effective - plus only computes once
        }

        /// <summary>
        /// See object.GetHashCode()
        /// </summary>
        public override int GetHashCode() => ToString().GetHashCode();

        /// <summary>
        /// See Object.ToString()
        /// </summary>
        public override string ToString()
        {
            if (toString != null) return toString;
            var sb = new StringBuilder().Append(NodeId).Append(" at ").Append(EndPoint);
            if (IsReplica)
            {
                sb.Append(", replica of ").Append(ParentNodeId);
                var parent = Parent;
                if (parent != null) sb.Append(" at ").Append(parent.EndPoint);
            }
            var childCount = Children.Count;
            switch(childCount)
            {
                case 0: break;
                case 1: sb.Append(", 1 replica"); break;
                default: sb.Append(", ").Append(childCount).Append(" replicas"); break;
            }
            if(Slots.Count != 0)
            {
                sb.Append(", slots: ");
                foreach(var slot in Slots)
                {
                    sb.Append(slot).Append(' ');
                }
                sb.Length--; // remove tailing space
            }
            return toString = sb.ToString();
        }

        internal bool ServesSlot(int hashSlot)
        {
            foreach (var slot in Slots)
            {
                if (slot.Includes(hashSlot)) return true;
            }
            return false;
        }

        int IComparable.CompareTo(object obj)
        {
            return CompareTo(obj as ClusterNode);
        }
    }
}
