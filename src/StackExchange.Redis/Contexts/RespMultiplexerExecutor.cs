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
        private readonly Func<int, RedisCommand, CommandFlags, RespExecutorBase?> _forSlot;
        private readonly Func<RedisCommand, CommandFlags, RespExecutorBase?> _any;
        private readonly Func<EndPoint, RespExecutorBase?>? _forEndpoint;
        private readonly Action<int, EndPoint>? _onSlotMoved;
        private readonly Action? _onTopologySuspect;

        /// <summary>Create a routing executor over a set of endpoints.</summary>
        /// <param name="topology">The cell that says whether slots mean anything yet.</param>
        /// <param name="forSlot">Resolves a hash slot to the endpoint that serves it.</param>
        /// <param name="any">Resolves the endpoint to use when no key steers the choice.</param>
        /// <remarks>
        /// <b>Both resolvers take the command and its flags</b>, not just a slot. Which node serves a
        /// request is not only a question of <i>where the data is</i>: <c>PreferReplica</c> on a read
        /// should reach a replica, and only the resolver knows which endpoints are replicas and which are
        /// reachable. Passing a slot alone silently routed everything to the primary.
        /// </remarks>
        /// <param name="database">The database commands run against.</param>
        /// <param name="forEndpoint">Resolves a redirect target to an executor, if redirects are followed.</param>
        /// <param name="onSlotMoved">Told when a <c>MOVED</c> reveals the slot map is stale.</param>
        /// <param name="onTopologySuspect">Told when a redirect could not be followed at all.</param>
        internal RespMultiplexerExecutor(
            RespTopology topology,
            Func<int, RedisCommand, CommandFlags, RespExecutorBase?> forSlot,
            Func<RedisCommand, CommandFlags, RespExecutorBase?> any,
            int database = 0,
            Func<EndPoint, RespExecutorBase?>? forEndpoint = null,
            Action<int, EndPoint>? onSlotMoved = null,
            Action? onTopologySuspect = null)
        {
            _topology = topology ?? throw new ArgumentNullException(nameof(topology));
            _forSlot = forSlot ?? throw new ArgumentNullException(nameof(forSlot));
            _any = any ?? throw new ArgumentNullException(nameof(any));
            Database = database;
            _forEndpoint = forEndpoint;
            _onSlotMoved = onSlotMoved;
            _onTopologySuspect = onTopologySuspect;
        }

        /// <summary>Follow a redirect, or decline it and let the reply stand as an error.</summary>
        /// <param name="redirect">What the server said.</param>
        /// <param name="operation">The command that was redirected; not yet completed.</param>
        /// <remarks>
        /// <para>
        /// <b>Called on a connection's IO loop</b>, so it must not block: resolving an endpoint and
        /// handing the operation to its executor is the whole of the work, and if that executor has no
        /// connection yet the operation lands in its backlog rather than waiting here.
        /// </para>
        /// <para>
        /// <b>The map is updated for <c>MOVED</c> and not for <c>ASK</c>, which is the entire difference
        /// between them.</b> <c>MOVED</c> says the slot has moved and our map is stale; <c>ASK</c> says
        /// this one key is mid-migration and the map is still right for everything else. Updating on
        /// <c>ASK</c> would point every subsequent key in the slot at the node that only holds the ones
        /// already migrated.
        /// </para>
        /// </remarks>
        internal bool TryFollowRedirect(in RespRedirect redirect, RespPayloadOperation operation)
        {
            if (redirect.IsUnroutable)
            {
                // the server does not know where the slot went either, so there is nowhere to send this.
                // Ask for a topology refresh, which CAN name the target, and let the error stand.
                _onTopologySuspect?.Invoke();
                return false;
            }

            var target = _forEndpoint?.Invoke(redirect.Endpoint!);
            if (target is null) return false;

            if (redirect.IsMoved) _onSlotMoved?.Invoke(redirect.Slot, redirect.Endpoint!);

            return redirect.IsMoved
                ? target.TryResend(operation)
                : target.TryResendAsking(operation);
        }

        /// <inheritdoc/>
        public override int Database { get; }

        /// <inheritdoc/>
        /// <remarks>
        /// Forwarded from wherever a command would actually go, because that is what decides it. Asking
        /// with no key is the honest approximation: a deployment whose endpoints disagreed about this
        /// would be one running two different cores at once.
        /// </remarks>
        public override bool CanCancel => ResolveFor(default, RedisCommand.NONE, CommandFlags.None) is { CanCancel: true };

        /// <inheritdoc/>
        internal override RespExecutorBase? ResolveFor(in RedisKey key, RedisCommand command, CommandFlags flags)
        {
            // the routing step, with no request to read a slot from - so the slot comes from the key, the
            // same way the writer would have computed it
            var target = _topology.RoutesBySlot && !key.IsNull
                ? _forSlot(ServerSelectionStrategy.GetHashSlot(key), command, flags)
                : _any(command, flags);
            return target?.ResolveFor(in key, command, flags);
        }

        /// <inheritdoc/>
        public override ValueTask<EndPoint?> IdentifyEndpointAsync(
            RedisKey key,
            CommandFlags flags,
            CancellationToken cancellationToken = default)
        {
            var target = _topology.RoutesBySlot && !key.IsNull
                ? _forSlot(ServerSelectionStrategy.GetHashSlot(key), RedisCommand.PING, flags)
                : _any(RedisCommand.PING, flags);
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
            // A write demanded on a replica is refused rather than routed. The rule about WHICH commands
            // are primary-only already exists and is shared with the interpolated writer
            // (Message.DemandPrimary), which is why this only has to ask rather than decide: a caller who
            // DEMANDED a replica for a write asked for something that cannot be honoured, as opposed to
            // expressing a preference that routing may override.
            if (Message.GetPrimaryReplicaFlags(request.Flags) == CommandFlags.DemandReplica
                && request.Command.IsPrimaryOnly())
            {
                ThrowPrimaryOnly(request.Command);
            }

            // THE fast path, and the reason topology is a volatile bool rather than anything richer.
            // Note the flags still travel: outside cluster there is no slot to resolve, but there may
            // still be a replica to prefer.
            if (!_topology.RoutesBySlot) return _any(request.Command, request.Flags) ?? ThrowNoEndpoint();

            var slot = request.Slot;
            if (slot == ServerSelectionStrategy.MultipleSlots) ThrowCrossSlot();

            // NoSlot in a cluster means the command names no key - PING, an admin command - so anywhere
            // will do. It no longer ALSO means "rendered before we knew": the topology's Unknown state
            // computes slots speculatively for exactly that reason, so a request that crossed the
            // discovery boundary still carries one.
            return (slot == ServerSelectionStrategy.NoSlot
                ? _any(request.Command, request.Flags)
                : _forSlot(slot, request.Command, request.Flags)) ?? ThrowNoEndpoint();
        }

        private static RespExecutorBase ThrowNoEndpoint()
            => throw new RedisConnectionException(
                ConnectionFailureType.UnableToResolvePhysicalConnection,
                CommandFlags.CommandRetryNever,
                "No endpoint is available to serve this command.",
                null,
                CommandStatus.WaitingToBeSent);

        private static void ThrowPrimaryOnly(RedisCommand command)
            => throw new RedisCommandException($"Command cannot be issued to a replica: {command}");

        private static void ThrowCrossSlot()
            => throw new RedisCommandException(
                "This command spans multiple hash slots, which a cluster cannot serve from one node.");
    }
}
