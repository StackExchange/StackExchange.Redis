using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Protocol;

namespace StackExchange.Redis
{
    /// <summary>
    /// EXPERIMENTAL SPIKE. Routes a command to an endpoint, by slot when that means anything.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <b>multiplexer</b> executor of the three in design notes section 3b. It makes one decision -
    /// which endpoint - and then hands the whole command to that endpoint's executor, which owns the
    /// connection. It does not re-implement connecting, reconnecting, or the backlog.
    /// </para>
    /// <para>
    /// <b>The non-cluster path costs a volatile read and a field read, and nothing else.</b> No hashing,
    /// no dictionary, no slot arithmetic - and not merely here: the request never had a slot computed in
    /// the first place, because <c>RespRequestBuilder</c> consults the same topology before hashing a key.
    /// Outside cluster, the whole apparatus is one branch that is always false.
    /// </para>
    /// <para>
    /// <b>What ordering this can promise, because it bounds everything else.</b> Per-connection FIFO is
    /// the primitive; per-slot ordering is what can be built on it, and nothing stronger is available to
    /// any multiplexed client - commands for different slots go to different nodes and run concurrently,
    /// so there is no global order to preserve. Hence: in the order issued on one connection; in the
    /// order issued for one slot, for as long as that slot's owner does not move; nothing across slots.
    /// That is why a cross-slot command is refused here rather than split - there is no order in which to
    /// do the halves that would mean anything.
    /// </para>
    /// <para>
    /// <b>Topology is read per send, never captured.</b> A multiplexer can be constructed and
    /// <c>GetDatabase()</c> called while still disconnected, and the resulting context is memoised for the
    /// life of the multiplexer - so an executor that decided its routing at construction would route a
    /// cluster as a single server forever. See <see cref="RespTopology"/>.
    /// </para>
    /// </remarks>
    internal sealed class RespMultiplexerExecutor : RespExecutorBase
    {
        private readonly RespTopology _topology;
        private readonly Func<int, RespExecutorBase?> _forSlot;
        private readonly Func<RespExecutorBase?> _any;

        /// <summary>Create a routing executor over a set of endpoints.</summary>
        /// <param name="topology">The cell that says whether slots mean anything yet.</param>
        /// <param name="forSlot">Resolves a hash slot to the endpoint that serves it.</param>
        /// <param name="any">Resolves the endpoint to use when no key steers the choice.</param>
        /// <param name="database">The database commands run against.</param>
        internal RespMultiplexerExecutor(
            RespTopology topology,
            Func<int, RespExecutorBase?> forSlot,
            Func<RespExecutorBase?> any,
            int database = 0)
        {
            _topology = topology ?? throw new ArgumentNullException(nameof(topology));
            _forSlot = forSlot ?? throw new ArgumentNullException(nameof(forSlot));
            _any = any ?? throw new ArgumentNullException(nameof(any));
            Database = database;
        }

        /// <inheritdoc/>
        public override int Database { get; }

        /// <inheritdoc/>
        public override bool IsConnected(in RedisKey key, CommandFlags flags)
        {
            // no request to read a slot from, so this asks the same question the router would: which
            // endpoint would take this key, and is it up
            var target = _topology.RoutesBySlot && !key.IsNull
                ? _forSlot(ServerSelectionStrategy.GetHashSlot(key))
                : _any();
            return target is not null && target.IsConnected(in key, flags);
        }

        /// <inheritdoc/>
        public override ValueTask<EndPoint?> IdentifyEndpointAsync(
            RedisKey key,
            CommandFlags flags,
            CancellationToken cancellationToken = default)
        {
            var target = _topology.RoutesBySlot && !key.IsNull
                ? _forSlot(ServerSelectionStrategy.GetHashSlot(key))
                : _any();
            return target is null ? default : target.IdentifyEndpointAsync(key, flags, cancellationToken);
        }

        /// <inheritdoc/>
        public override RespPayload Send(in RespRequest request) => Route(in request).Send(in request);

        /// <inheritdoc/>
        public override ValueTask<RespPayload> SendAsync(RespRequest request, CancellationToken cancellationToken = default)
            => Route(in request).SendAsync(request, cancellationToken);

        /// <summary>Pick the endpoint for one command.</summary>
        /// <param name="request">The rendered request, carrying its combined slot.</param>
        private RespExecutorBase Route(in RespRequest request)
        {
            // THE fast path, and the reason topology is a volatile bool rather than anything richer
            if (!_topology.RoutesBySlot) return _any() ?? ThrowNoEndpoint();

            var slot = request.Slot;
            if (slot == ServerSelectionStrategy.MultipleSlots) ThrowCrossSlot();

            // NoSlot in a cluster means the command names no key - PING, an admin command - so anywhere
            // will do. It no longer ALSO means "rendered before we knew": the topology's Unknown state
            // computes slots speculatively for exactly that reason, so a request that crossed the
            // discovery boundary still carries one.
            return (slot == ServerSelectionStrategy.NoSlot ? _any() : _forSlot(slot)) ?? ThrowNoEndpoint();
        }

        private static RespExecutorBase ThrowNoEndpoint()
            => throw new RedisConnectionException(
                ConnectionFailureType.UnableToResolvePhysicalConnection,
                CommandFlags.CommandRetryNever,
                "No endpoint is available to serve this command.",
                null,
                CommandStatus.WaitingToBeSent);

        private static void ThrowCrossSlot()
            => throw new RedisCommandException(
                "This command spans multiple hash slots, which a cluster cannot serve from one node.");
    }
}
