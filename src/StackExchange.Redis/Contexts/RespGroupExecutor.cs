using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Protocol;

namespace StackExchange.Redis
{
    /// <summary>
    /// Sends to whichever member of a connection group is currently active.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <b>group</b> executor of the three in design notes section 3b, and the thinnest of them: it
    /// resolves the active member and hands the command over. Which member is active - weight, latency,
    /// health, an explicit failover - is the group's decision and stays there; this only has to ask at
    /// the right moment.
    /// </para>
    /// <para>
    /// <b>Resolved per send, never captured</b>, for the same reason the multiplexer executor re-reads
    /// topology per send: the whole point of a group is that the active member changes, and a context is
    /// memoised for the life of the multiplexer. An executor that resolved once would pin itself to
    /// whichever member happened to be active when somebody first called <c>GetDatabase()</c>, which is
    /// the opposite of what a group is for.
    /// </para>
    /// <para>
    /// <b>An active-member switch is a bump, and that is consistent rather than convenient.</b> Commands
    /// already sent to the old member stay there and complete - or fail - against it; commands issued
    /// after the switch go to the new one. Ordering across the switch is not preserved, which is exactly
    /// what the ordering contract excludes: the guarantee is per connection, and a failover is a change of
    /// connection. Same reasoning as a reshard, and as a cluster/standalone reconfiguration.
    /// </para>
    /// </remarks>
    internal sealed class RespGroupExecutor : RespExecutorBase
    {
        private readonly Func<RespExecutorBase?> _active;
        private readonly Func<string>? _describeUnavailable;
        private readonly Func<CancellationToken>? _nextFailover;

        /// <summary>Create an executor over a group of members.</summary>
        /// <param name="active">Resolves the currently active member's executor, or null if none is.</param>
        /// <param name="database">The database commands run against.</param>
        /// <param name="describeUnavailable">
        /// Supplies the detail for the "nothing is reachable" exception; the group knows why and this does
        /// not.
        /// </param>
        /// <param name="nextFailover">Where to get the token that cancels on the next failover, if this group tracks one.</param>
        internal RespGroupExecutor(
            Func<RespExecutorBase?> active,
            int database = 0,
            Func<string>? describeUnavailable = null,
            Func<CancellationToken>? nextFailover = null)
        {
            _active = active ?? throw new ArgumentNullException(nameof(active));
            Database = database;
            _describeUnavailable = describeUnavailable;
            _nextFailover = nextFailover;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// <b>The group is where failover actually happens</b>, so it is the one executor that can answer
        /// this: moving between members IS the failover, and the group is what moves. Everything below is
        /// a single multiplexer, which cannot fail over to anything, and everything above just forwards.
        /// </remarks>
        internal override Func<CancellationToken>? GetFailoverSource() => _nextFailover;

        /// <inheritdoc/>
        public override int Database { get; }

        /// <summary>The group this executor serves, reported in place of whichever member is active.</summary>
        internal IConnectionMultiplexer? Group { get; init; }

        /// <inheritdoc/>
        /// <remarks>
        /// <b>The group, not the active member</b>: a library-name suffix added through it reaches every member,
        /// and is remembered for any that join later, where one added to the active member would be lost at
        /// the next failover.
        /// </remarks>
        internal override IConnectionMultiplexer? ExposedMultiplexer => Group ?? _active()?.ExposedMultiplexer;

        /// <inheritdoc/>
        /// <remarks>Forwarded from the active member; a group does not send anything itself.</remarks>
        public override bool CanCancel => _active() is { CanCancel: true };

        /// <inheritdoc/>
        public override bool CanWritePreamble => _active() is { CanWritePreamble: true };

        /// <inheritdoc/>
        /// <remarks>
        /// <b>Forwarded, like the other capabilities.</b> Not forwarding it meant a view over a batch or a
        /// transaction answered "no, I send each command as it arrives" while queueing every one of them -
        /// and the answer is consulted for correctness, not tuning: <c>Scripts</c> asks it to decide
        /// whether to inline a script body, so over a wrapper it would pair SCRIPT LOAD with EVALSHA
        /// inside MULTI/EXEC and shift every result in the EXEC array.
        /// </remarks>
        internal override bool Accumulates => _active() is { Accumulates: true };

        /// <inheritdoc/>
        internal override bool Transactional => _active() is { Transactional: true };

        /// <inheritdoc/>
        public override ValueTask<RespPayload> SendAsync(
            RespRequest preamble,
            RespRequest request,
            IRespPreambleGate? gate,
            CancellationToken cancellationToken = default)
            => Active().SendAsync(preamble, request, gate, cancellationToken);

        /// <inheritdoc/>
        internal override RespPayload Send(RespRequest preamble, RespRequest request, IRespPreambleGate? gate)
            => Active().Send(preamble, request, gate);

        /// <inheritdoc/>
        internal override bool CanWriteRuns => _active() is { CanWriteRuns: true };

        /// <inheritdoc/>
        internal override bool CanWriteTransactions => _active() is { CanWriteTransactions: true };

        /// <summary>Whether any member is currently able to serve.</summary>
        public bool HasActiveMember => _active() is not null;

        /// <inheritdoc/>
        /// <remarks>
        /// <see langword="false"/> when the group is fully down, rather than a throw: "can you reach this
        /// key?" has a perfectly good answer in that state, and it is no.
        /// </remarks>
        internal override RespExecutorBase? ResolveFor(in RedisKey key, RedisCommand command, CommandFlags flags)
            => _active()?.ResolveFor(in key, command, flags);

        /// <inheritdoc/>
        /// <remarks>Null when nothing is active; the same "no honest answer" default as elsewhere.</remarks>
        public override ValueTask<EndPoint?> IdentifyEndpointAsync(
            RedisKey key,
            CommandFlags flags,
            CancellationToken cancellationToken = default)
            => _active() is { } active ? active.IdentifyEndpointAsync(key, flags, cancellationToken) : default;

        /// <inheritdoc/>
        public override RespPayload Send(in RespRequest request) => Active().Send(in request);

        /// <inheritdoc/>
        public override ValueTask<RespPayload> SendAsync(RespRequest request, CancellationToken cancellationToken = default)
            => Active().SendAsync(request, cancellationToken);

        private RespExecutorBase Active() => _active() ?? ThrowUnavailable();

        /// <summary>Every member is down, so there is nothing to send to.</summary>
        /// <remarks>
        /// A <see cref="RedisConnectionException"/> rather than an <see cref="InvalidOperationException"/>,
        /// matching what the shipped group facade throws: a group is an
        /// <see cref="IConnectionMultiplexer"/>, a plain one reports this situation that way, and a caller
        /// catching <see cref="RedisConnectionException"/> should not have to know which kind it was
        /// handed. See #3223.
        /// </remarks>
        private RespExecutorBase ThrowUnavailable()
            => throw new RedisConnectionException(
                ConnectionFailureType.UnableToResolvePhysicalConnection,
                CommandFlags.CommandRetryNever,
                _describeUnavailable?.Invoke() ?? "No connection group member is available.",
                null,
                CommandStatus.WaitingToBeSent); // never sent, so retry is free to re-issue it
    }
}
