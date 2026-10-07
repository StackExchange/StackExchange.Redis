using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using RESPite;
using RESPite.Messages;
using RESPite.Operations;
using StackExchange.Redis.Caching;
using StackExchange.Redis.Protocol;

namespace StackExchange.Redis
{
    /// <summary>
    /// Something that can issue a rendered request.
    /// </summary>
    /// <remarks>
    /// <b>Internal.</b> Dispatch is an implementation concern; the public surface is the context and the
    /// extension members over it. Keeping this internal means the executor chain - retry, and whatever
    /// follows - can be reshaped without it being a breaking change.
    /// </remarks>
    /// <remarks>
    /// <para>
    /// <b>Neither side is a span, and neither side is a <c>byte[]</c>.</b> A span cannot cross an
    /// <c>await</c>, and cannot be parked in a backlog for a resend after a reconnect - so a span request
    /// rules out async and rules out retries even when synchronous. A <c>byte[]</c> reply allocates on
    /// every call, which is the cost this whole design exists to remove. Both sides are therefore pooled
    /// and reference-counted: <see cref="RespRequest"/> in, <see cref="RespPayload"/> out.
    /// </para>
    /// <para>
    /// <b>Ownership.</b> The caller owns one reference to the request and releases it when the call
    /// completes; an implementation that needs the bytes for longer - a backlog, a resend, an unflushed
    /// write - takes its own with <see cref="RespRequest.TryRetain"/>. The reply is returned with one
    /// reference held by the caller, who releases it. Whoever retains, releases.
    /// </para>
    /// </remarks>
    /// <remarks>
    /// <para>
    /// <b>A class rather than an interface, and internal rather than public.</b> Nothing outside this
    /// assembly implements an executor - the public surface is <see cref="RespExecutor"/>'s extension
    /// methods over the contexts, which is all an external caller needs to issue a command - so the
    /// abstraction can stay closed. What that buys beyond tidiness is the two capabilities below: as
    /// <c>virtual</c> members with defaults they are a decision every executor inherits and can override,
    /// where as separate interfaces they were type-tested at the call site, and an executor that simply
    /// did not implement one was silently downgraded rather than asked.
    /// </para>
    /// </remarks>
    internal abstract class RespExecutorBase
    {
        /// <summary>The database requests run against; part of a cached entry's identity.</summary>
        public abstract int Database { get; }

        /// <summary>The multiplexer this executor belongs to, where it belongs to one.</summary>
        /// <remarks>
        /// <b>Only for reporting a fault that is about the client rather than the command.</b> An executor
        /// built directly over a transport - as the tests do - has no multiplexer, and nothing here may
        /// depend on having one.
        /// </remarks>
        internal virtual ConnectionMultiplexer? Multiplexer => null;

        /// <summary>Issue the request and return the reply, with one reference held by the caller.</summary>
        /// <param name="request">The rendered request; retain it if it must outlive this call.</param>
        public abstract RespPayload Send(in RespRequest request);

        /// <summary>Issue the request asynchronously.</summary>
        /// <param name="request">The rendered request; retain it if it must outlive this call.</param>
        /// <param name="cancellationToken">Cancels the send.</param>
        public abstract ValueTask<RespPayload> SendAsync(RespRequest request, CancellationToken cancellationToken = default);

        /// <summary>Issue the request and parse its reply.</summary>
        /// <typeparam name="TResult">What the handler makes of the reply.</typeparam>
        /// <param name="request">The rendered request; consumed by this call on every path.</param>
        /// <param name="handler">Parses the reply.</param>
        /// <param name="cancellationToken">Cancels the send.</param>
        /// <returns>The parsed reply.</returns>
        /// <remarks>
        /// <para>
        /// <b>The default awaits the payload and parses it in an async method</b>, which suspends and so boxes
        /// its state machine whenever the reply is not already there. The pooling builder recycles those boxes
        /// only while few are alive at once; a deep pipeline keeps hundreds of thousands in flight, and then
        /// every send allocates one - measured at ~285 bytes, a third of everything a send allocated.
        /// </para>
        /// <para>
        /// An executor that owns the operation overrides this to make the operation itself the awaitable for
        /// the parsed result (<see cref="RespPayloadOperation{TResult}"/>), so a send is one object rather than
        /// two. Everything else - retry, batches, transactions, decorators - keeps this default.
        /// </para>
        /// </remarks>
        internal virtual ValueTask<TResult> SendTypedAsync<TResult>(
            RespRequest request, IRespHandler<TResult> handler, CancellationToken cancellationToken)
            => RespExecutor.AwaitUncached(this, request, handler, cancellationToken);

        /// <summary>
        /// Whether <see cref="SendTypedAsync{TResult}"/> has finished with the request's bytes when it returns,
        /// so it may be handed a BORROWED view of the caller's frame rather than a lease.
        /// </summary>
        /// <remarks>
        /// True only where the operation copies the request during the call (see
        /// <c>RespPayloadOperation.Attach</c>). The default holds the request across an await, and anything
        /// that queues before sending - a batch, a transaction, retry - may read it later, so it is false.
        /// </remarks>
        internal virtual bool CopiesRequestOnSend => false;

        /// <summary>Issue the request and check its reply succeeded, completing with nothing.</summary>
        /// <param name="request">The rendered request; consumed by this call on every path.</param>
        /// <param name="handler">Checks the reply.</param>
        /// <param name="cancellationToken">Cancels the send.</param>
        /// <returns>Completes when the reply has been checked.</returns>
        /// <remarks>The default adapts the typed send; an executor that owns the operation returns it directly.</remarks>
        internal virtual ValueTask SendVoidAsync(RespRequest request, IRespHandler<bool> handler, CancellationToken cancellationToken)
        {
            var pending = SendTypedAsync(request, handler, cancellationToken);
            return pending.IsCompletedSuccessfully ? default : Awaited(pending);

            static async ValueTask Awaited(ValueTask<bool> pending) => await pending.ConfigureAwait(false);
        }

        /// <summary>Whether this executor can honour a <see cref="CancellationToken"/> once a command is sent.</summary>
        /// <remarks>
        /// <para>
        /// <b>Asked rather than assumed</b>, exactly as <see cref="CanWritePreamble"/> is. The default is
        /// <see langword="false"/>, which is the honest answer for anything built on the classic pipeline:
        /// it cannot withdraw a request that has reached the socket, and cannot ignore the reply that is
        /// coming without desynchronising every reply after it.
        /// </para>
        /// <para>
        /// A decorator should forward this rather than answer for itself - whether a command can be
        /// cancelled is a property of the thing that finally sends it.
        /// </para>
        /// </remarks>
        public virtual bool CanCancel => false;

        /// <summary>Which executor would actually serve this command.</summary>
        /// <param name="key">The key being addressed, or default when nothing steers the choice.</param>
        /// <param name="command">The command, which can change the answer - see <c>BITFIELD_RO</c>.</param>
        /// <param name="flags">The flags, which can steer to a replica.</param>
        /// <returns>The executor that would serve it, or null when nothing can.</returns>
        /// <remarks>
        /// <para>
        /// <b>The one resolution primitive, because there turned out to be three questions asking it.</b>
        /// "Can you reach this key?", "what version is the server that would answer?" and "which endpoint
        /// would take it?" are the same walk down the executor chain with a different question at the
        /// bottom. Written separately they were six near-identical overrides each; written once they are
        /// one override per executor and the questions come free.
        /// </para>
        /// <para>
        /// A <b>decorator</b> - retry, batch - forwards, because it changes nothing about where a command
        /// goes. A <b>router</b> - group, multiplexer - resolves one step and recurses. An <b>endpoint</b>
        /// is the answer, and the default returns <see langword="this"/> for exactly that reason.
        /// </para>
        /// <para>
        /// Note what this is <i>not</i>: it does not go to the wire, and must not. It answers what routing
        /// <i>would</i> do, which is why <c>IdentifyEndpointAsync</c> - which asks what actually
        /// <i>did</i> answer - remains separate. That asymmetry is discussed in the queue.
        /// </para>
        /// </remarks>
        internal virtual RespExecutorBase? ResolveFor(in RedisKey key, RedisCommand command, CommandFlags flags) => this;

        /// <summary>
        /// The executor that serves a slot that has <b>already been computed</b>, rather than one derived
        /// from a key.
        /// </summary>
        /// <param name="slot">The hash slot, or <see cref="ServerSelectionStrategy.NoSlot"/>.</param>
        /// <param name="command">The command, whose flags can steer primary/replica choice.</param>
        /// <param name="flags">The command's flags.</param>
        /// <remarks>
        /// <b>A run has a slot but no single key.</b> A batch or a transaction covers many commands, and
        /// what decides where it goes is the slot they agree on - which each operation already carries,
        /// folded when it was rendered. Resolving from a key would mean picking one of them arbitrarily,
        /// and resolving from no key at all - which is what both did - means a cluster sends the run to
        /// whichever node answers first, keys or no keys.
        /// </remarks>
        internal virtual RespExecutorBase? ResolveForSlot(int slot, RedisCommand command, CommandFlags flags) => this;

        /// <summary>Where a publish for this channel would rather go, or <see langword="null"/> for no preference.</summary>
        /// <param name="channel">The channel being published to, <b>before</b> any channel prefix.</param>
        /// <remarks>
        /// <para>
        /// <b>Not the same question as <see cref="ResolveFor(in RedisKey, RedisCommand, CommandFlags)"/></b>,
        /// which asks where a key lives. This asks where <i>this client's own subscription</i> lives, and
        /// the answer is client state rather than topology: nothing about the channel says it.
        /// </para>
        /// <para>
        /// It matters because <c>PUBLISH</c> reports <b>how many clients that node delivered to</b>, not
        /// how many the cluster did. A publish sent to any other node is still delivered - it crosses the
        /// cluster bus - but answers 0, so a caller reading the count sees its own message vanish.
        /// <c>ClusterTests.ClusterPubSub</c> asserts exactly that, and says why in a comment.
        /// </para>
        /// <para>
        /// <see langword="null"/> is the right answer for most executors, and for all the composing ones:
        /// a batch or transaction is already going to one server as a unit, so a single command inside it
        /// has no say. Sharded and key-routed channels do not need this at all - they carry a real slot,
        /// and the slot is authoritative.
        /// </para>
        /// </remarks>
        internal virtual RespExecutorBase? ResolveForChannel(in RedisChannel channel) => null;

        /// <summary>
        /// A source for "the token that cancels when the next failover happens", or <see langword="null"/>
        /// when this chain has no notion of failing over.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>A source, not a token.</b> The token is replaced each time a failover happens - that is how
        /// it signals - so anything that captured one would be holding the previous failover's, already
        /// cancelled or never to be. It has to be re-fetched per use, which is what handing back a
        /// delegate forces.
        /// </para>
        /// <para>
        /// <see langword="null"/> rather than a source returning <see cref="CancellationToken.None"/>,
        /// because the two answers mean different things to a retry policy: "there is no failover here"
        /// makes the failover rungs unreachable, where a token that never fires would leave the policy
        /// waiting for one that cannot come. <c>RetryController.TracksFailover</c> is derived from exactly
        /// this distinction.
        /// </para>
        /// </remarks>
        internal virtual Func<CancellationToken>? GetFailoverSource() => null;

        /// <summary>Whether <b>this</b> executor, already resolved, can currently reach its server.</summary>
        /// <param name="key">The key being addressed.</param>
        /// <param name="flags">The command's flags.</param>
        /// <remarks>
        /// Only meaningful on a resolved executor; routers answer by resolving first. The default is
        /// <see langword="true"/> for the same reason <see cref="IsConnected"/>'s was: an executor with no
        /// notion of connectivity can reach the only thing it has.
        /// </remarks>
        internal virtual bool IsReachable(in RedisKey key, CommandFlags flags) => true;

        /// <summary>How close this executor is to being able to serve the given key.</summary>
        /// <param name="key">The key whose routing is being asked about; may be null for "anywhere".</param>
        /// <param name="flags">The flags that would be used, which can steer to a replica.</param>
        /// <remarks>
        /// <b>Derived from <see cref="IsReachable"/> by default, not answered independently.</b> An
        /// executor that knows only whether it can be reached - a fake, a stream over one socket - must
        /// not have that answer ignored because it did not know about this richer question. Only an
        /// executor that can actually tell "not dialled" from "failing" overrides this.
        /// </remarks>
        internal virtual RespConnectionState ConnectionStateNow(in RedisKey key, CommandFlags flags)
            => IsReachable(in key, flags) ? RespConnectionState.Connected : RespConnectionState.Deferred;

        /// <summary>What the server that would serve this command can do.</summary>
        /// <param name="command">The command, whose routing decides which server answers.</param>
        /// <param name="key">The key being addressed, or default when nothing steers the choice.</param>
        /// <param name="flags">The flags, which can steer to a replica of a different version.</param>
        /// <param name="features">The answer, when one is available.</param>
        /// <returns>Whether this is an observation rather than a guess.</returns>
        /// <remarks>
        /// <b>The third question on the same walk</b>, and the reason <see cref="ResolveFor"/> exists at
        /// all. Several commands are <i>chosen</i> from the answer - an all-GET <c>BITFIELD</c> goes out
        /// as <c>BITFIELD_RO</c> when the server has it, which is what lets a replica serve it - so a
        /// wrong answer here is not a missing optimisation, it is a command refused for a read.
        /// </remarks>
        internal bool TryGetFeatures(RedisCommand command, in RedisKey key, CommandFlags flags, out RedisFeatures features)
        {
            if (ResolveFor(in key, command, flags) is { } target && target.TryGetLocalFeatures(out features))
            {
                return true;
            }

            features = default;
            return false;
        }

        /// <summary>What <b>this</b> executor, already resolved, knows about its own server.</summary>
        /// <param name="features">The answer, when this executor has one.</param>
        /// <remarks>
        /// The default is "no idea", which is honest: an executor with no server behind it has nothing to
        /// report, and the caller's fallback - the configured default version - is a better guess than
        /// anything this could invent.
        /// </remarks>
        internal virtual bool TryGetLocalFeatures(out RedisFeatures features)
        {
            features = default;
            return false;
        }

        /// <summary>The connection this executor would use right now, when it owns one.</summary>
        /// <remarks>
        /// <para>
        /// <b>Exists for <c>WATCH</c>, and should not grow other callers.</b> Almost everything here is
        /// deliberately connection-agnostic: an executor is asked to send, and which socket it picks is
        /// its business. <c>WATCH</c> is the exception, because the guarantee it makes <i>is</i>
        /// connection state - the watch and the <c>EXEC</c> that relies on it have to happen on the same
        /// one, and the only way to promise that is to name it.
        /// </para>
        /// <para>
        /// Null means "not one I can name", which is the honest answer for a router: it has not resolved
        /// yet, and resolving needs a key. Callers resolve first.
        /// </para>
        /// </remarks>
        internal virtual RespConnection? CurrentConnection => null;

        /// <summary>Give back the write slot acquired by <see cref="PrepareRunAsync"/>, and let anything that queued behind it go.</summary>
        internal virtual void ReleaseWrites()
        {
        }

        /// <summary>The same target, sending to a different database; null if it cannot be re-pointed.</summary>
        /// <param name="database">The database index, or -1 for "no database".</param>
        /// <remarks>
        /// <b>Asked rather than assumed, because only some executors can move.</b> A database is a property
        /// of what finally writes: one that owns a connection can offer a view over it per database, and one
        /// that composes - a batch, a transaction - cannot, because its run is already committed to a
        /// database. Null is the honest answer for those, and <see cref="RespContext.WithDatabase"/> turns it
        /// into a refusal that names the type rather than a silent move that lies about where a command went.
        /// </remarks>
        internal virtual RespExecutorBase? WithDatabase(int database) => Database == database ? this : null;

        /// <summary>Whether this executor times out an operation itself, with a diagnostic.</summary>
        /// <remarks>
        /// <b>About which exception a caller sees, not whether one arrives.</b> An executor that answers
        /// true will fail a stalled operation on its own terms - with the command, the endpoint, the
        /// backlog depth and the last connection fault - which is the text people paste into issues when
        /// something goes wrong at three in the morning. A synchronous waiter that imposes its own timer
        /// on top of that races it and usually wins, and what it raises is a bare
        /// <see cref="TimeoutException"/> saying "the operation has timed out" and nothing else.
        /// <para>
        /// So a waiter asks first, and stands back when the answer is yes. The default is false, which
        /// keeps the outer timeout for anything that cannot promise to time itself out - a fake in a test,
        /// or an executor that has not grown a sweep yet. Better a poor exception than a hang.
        /// </para>
        /// </remarks>
        internal virtual bool EnforcesTimeouts => false;

        /// <summary>Whether this executor can write a contiguous run at all.</summary>
        /// <remarks>
        /// <b>Asked before a batch or transaction is built, not discovered when it is sent.</b> A run
        /// needs a connection to write to, and the <c>Message</c> shim had none - it reached the server
        /// through the old pipeline - so a batch composed over it could only fail. Answering the question up
        /// front lets the caller choose another implementation instead, which is exactly what a
        /// transitional database with a fallback should do.
        /// <para>
        /// Forwarded rather than assumed by routers, as <see cref="CanCancel"/> is: whether a run can be
        /// written is a property of whatever finally writes it.
        /// </para>
        /// </remarks>
        internal virtual bool CanWriteRuns => false;

        /// <summary>Wait until this executor can accept a contiguous run, connecting if it must.</summary>
        /// <param name="cancellationToken">Cancels the wait.</param>
        /// <returns>Whether a run can now be written.</returns>
        /// <remarks>
        /// <b>Separate from <see cref="TrySendBatch"/> because that one cannot wait.</b> It returns a
        /// bool, so an endpoint with no connection yet can only decline - and declining fails the caller
        /// for a batch that merely arrived early, which is the wrong answer when the honest one is "wait
        /// for the connection, then write the run contiguously". The default is true, for executors that
        /// have nothing to wait for.
        /// </remarks>
        internal virtual ValueTask<bool> PrepareRunAsync(CancellationToken cancellationToken = default)
            => new(true);

        /// <summary>Write a run of operations contiguously, as a batch.</summary>
        /// <param name="operations">The operations, in the order they should reach the server.</param>
        /// <returns>Whether this executor took them on.</returns>
        /// <remarks>
        /// Returns nothing but a bool, because there is nothing to return: each operation is its own
        /// completion and the callers already hold their handles.
        /// </remarks>
        internal virtual bool TrySendBatch(List<RespPayloadOperation> operations) => false;

        /// <summary>Send a preamble on its own, awaiting it, when it could not be paired with its request.</summary>
        /// <param name="preamble">The preamble frame.</param>
        /// <param name="gate">The condition the preamble establishes, to be told if it succeeds.</param>
        /// <param name="cancellationToken">Cancels the send.</param>
        /// <remarks>
        /// <b>Not the same as sending it as a command, which is what this replaces, and the difference is
        /// two bugs.</b> A preamble is the client's own machinery: it does not belong in a profiling
        /// session, where it appeared as a <c>SCRIPT</c> the caller never issued and could not reproduce;
        /// and its success is what the gate exists to remember, so not telling the gate left the belief
        /// unset in exactly the case this path always covers - the first evaluation, before a connection is
        /// warm enough to pair on. Every later call then re-sent the preamble, forever, correctly and
        /// pointlessly.
        /// <para>
        /// The default here can do neither, having no connection to record against and no profiling to
        /// suppress; whoever owns a connection overrides it.
        /// </para>
        /// </remarks>
        internal virtual async ValueTask SendPreambleAsync(
            RespRequest preamble, IRespPreambleGate? gate, CancellationToken cancellationToken = default)
            => (await SendAsync(preamble, cancellationToken).ForAwait())?.Release();

        /// <summary>
        /// Write an already-assembled run to <paramref name="connection"/>, adding whatever this executor
        /// needs in front of it.
        /// </summary>
        /// <param name="connection">The connection to write to.</param>
        /// <param name="run">The operations, in order.</param>
        /// <param name="count">How many of <paramref name="run"/> to write.</param>
        /// <remarks>
        /// <b>The seam a transaction needs.</b> A transaction assembles its own run - <c>MULTI</c>, the
        /// queued commands, <c>EXEC</c> - and writes it straight to the connection, so it never passes
        /// through the executor that would otherwise know a <c>SELECT</c> is due. Asking the executor to do
        /// the writing puts that knowledge back in the path without the transaction having to hold any of
        /// it: the <c>SELECT</c> goes in front of <c>MULTI</c>, which is the only place it can go - inside
        /// the transaction it would be queued and applied at <c>EXEC</c> like any other command.
        /// </remarks>
        internal virtual bool TryWriteRun(RespConnection connection, IRespMessage[] run, int count)
            => connection.Send(run, count);

        /// <summary>
        /// Whether this executor can run a <c>MULTI</c>/<c>EXEC</c> transaction, which is a <b>stronger</b>
        /// claim than <see cref="CanWriteRuns"/>.
        /// </summary>
        /// <remarks>
        /// <b>Two capabilities because there are two mechanisms.</b> A batch needs its commands written
        /// consecutively and nothing more. A transaction needs that AND a connection it can hold across
        /// several writes - the watches, the checks, then MULTI/EXEC - with a write claim keeping other
        /// senders out in between. An executor can honestly offer the first without the second: the
        /// <c>Message</c> shim writes a run through <c>IMultiMessage</c>, which the old bridge expands
        /// inside its write lock, but it has no <c>RespConnection</c> to hold.
        /// <para>
        /// Conflating them made a transaction over that shim hang rather than fall back: it was told the
        /// executor could serve one, and then found no connection to serve it on.
        /// </para>
        /// </remarks>
        internal virtual bool CanWriteTransactions => false;

        /// <summary>Write <c>MULTI</c>, a run of queued commands, and <c>EXEC</c>, contiguously.</summary>
        /// <param name="operations">The queued commands, in order.</param>
        /// <param name="exec">Completes with whether the transaction executed.</param>
        /// <returns>Whether this executor took it on.</returns>
        /// <remarks>
        /// One run, because <c>MULTI</c> is per-connection state: anything of anybody else's interleaved
        /// between the <c>MULTI</c> and the <c>EXEC</c> would join the transaction rather than run beside
        /// it. That is a stronger requirement than a batch's, which only wants the commands adjacent.
        /// </remarks>
        internal virtual bool TrySendTransaction(List<RespPayloadOperation> operations, out ValueTask<bool> exec)
        {
            exec = default;
            return false;
        }

        /// <summary>Re-issue an operation that a redirect sent here, without completing it first.</summary>
        /// <param name="operation">The operation; still pending, and still owning its request bytes.</param>
        /// <returns>Whether this executor took it on.</returns>
        /// <remarks>
        /// Distinct from <c>SendAsync</c> because there is nothing to return: the operation <i>is</i> the
        /// completion, and whoever is awaiting it already holds their handle. The default declines, so an
        /// executor that cannot re-issue lets the redirect stand as the error it arrived as.
        /// </remarks>
        internal virtual bool TryResend(RespPayloadOperation operation) => false;

        /// <summary>Re-issue an operation preceded by <c>ASKING</c>, with nothing between the two.</summary>
        /// <param name="operation">The operation; still pending, and still owning its request bytes.</param>
        /// <returns>Whether this executor took it on.</returns>
        /// <remarks>
        /// <c>ASKING</c> applies to the very next command on that connection, so the pair has to be
        /// written as one - anything interleaved would consume it instead.
        /// </remarks>
        internal virtual bool TryResendAsking(RespPayloadOperation operation) => false;

        /// <summary>Whether a connected server is currently available to serve the given key.</summary>
        /// <param name="key">The key whose routing is being asked about; may be null for "anywhere".</param>
        /// <param name="flags">The flags that would be used, which can steer to a replica.</param>
        /// <remarks>
        /// <para>
        /// <b>A routing question, not a command.</b> Nothing is sent - this asks the router what it would
        /// do, which is the one thing the context surface cannot work out for itself and the reason
        /// <c>IsConnected</c> sat on the fallback. It belongs here because the executor <i>is</i> the
        /// router: the topology in design notes section 3b is three executors that differ only in how
        /// they resolve a key to a connection.
        /// </para>
        /// <para>
        /// The default is <see langword="true"/>: an executor with no routing of its own - a fake, a
        /// stream over one socket - can always reach the only server it has. An executor that routes and
        /// says nothing would otherwise claim to be disconnected, which is the more damaging wrong answer.
        /// </para>
        /// </remarks>
        public bool IsConnected(in RedisKey key, CommandFlags flags)
            => ResolveFor(in key, RedisCommand.PING, flags) is { } target && target.IsReachable(in key, flags);

        /// <summary>The routing question, answered with the detail a boolean cannot carry.</summary>
        /// <param name="key">The key whose routing is being asked about; null means "anywhere".</param>
        /// <param name="flags">
        /// The flags that would be used. <b>Required, not optional</b>: which endpoint serves a key depends
        /// on the primary/replica preference, so without them this would answer about a different endpoint
        /// from the one a command would actually use - and <c>DemandReplica</c> with no replica is
        /// <see cref="RespConnectionState.Unroutable"/> rather than merely unconnected.
        /// </param>
        /// <remarks>
        /// <b>Additive: <see cref="IsConnected"/> is deliberately left alone.</b> It is public API with a
        /// documented meaning and no internal caller depends on it, so widening it would be a behaviour
        /// change for existing users in exchange for very little. This answers the question it could not.
        /// </remarks>
        internal RespConnectionState GetConnectionState(in RedisKey key, CommandFlags flags)
            => ResolveFor(in key, RedisCommand.PING, flags) is { } target
                ? target.ConnectionStateNow(in key, flags)
                : RespConnectionState.Unroutable;

        /// <summary>Which endpoint would serve - or did serve - a command for the given key.</summary>
        /// <param name="key">The key whose routing is being asked about; null means "anywhere".</param>
        /// <param name="flags">The flags that would be used, which can steer to a replica.</param>
        /// <param name="cancellationToken">Cancels the probe.</param>
        /// <remarks>
        /// <para>
        /// <b>Unlike <see cref="IsConnected"/>, this one has to go to the wire</b>, and that is not an
        /// implementation detail - it is the definition. The question is which connection <i>answered</i>,
        /// so a routing prediction would be a different and weaker answer: under a cluster reshard or a
        /// failover, what routing would have chosen and what actually replied can differ.
        /// </para>
        /// <para>
        /// The default is <see langword="null"/> - "no idea" - because an executor with no notion of
        /// endpoints has no honest answer, and inventing one would be worse than admitting it.
        /// </para>
        /// </remarks>
        public virtual ValueTask<EndPoint?> IdentifyEndpointAsync(
            RedisKey key,
            CommandFlags flags,
            CancellationToken cancellationToken = default)
            => default;

        /// <summary>Whether <see cref="SendAsync(RespRequest, RespRequest, IRespPreambleGate?, CancellationToken)"/> does anything useful.</summary>
        /// <remarks>
        /// Asked rather than type-tested. The default answer is no, and the default implementation throws,
        /// so an executor that has not thought about preambles is not quietly assumed to support them.
        /// </remarks>
        public virtual bool CanWritePreamble => false;

        /// <summary>
        /// Whether this executor <b>queues</b> commands and sends them later, rather than sending each as
        /// it arrives.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>The question is whether a reply can be awaited before the next command is composed</b>, and
        /// for a batch or a transaction it cannot: nothing is sent until the run is, so awaiting anything
        /// mid-composition waits for a send that this very code is holding up.
        /// </para>
        /// <para>
        /// It matters for preambles. An executor that cannot pair falls back to sending the two in
        /// sequence and awaiting the first - correct, and one round trip worse - but inside an accumulating
        /// executor that await cannot complete until the run goes out, and by then the second command has
        /// nowhere to be sent. A caller with another way to express itself should take it: a script, for
        /// instance, can carry its own body instead of a hash and a <c>SCRIPT LOAD</c>.
        /// </para>
        /// </remarks>
        internal virtual bool Accumulates => false;

        /// <summary>
        /// Whether this executor wraps what it sends in <c>MULTI</c>/<c>EXEC</c>.
        /// </summary>
        /// <remarks>
        /// <b>Narrower than <see cref="Accumulates"/>, and the difference is load-bearing.</b> A batch also
        /// accumulates, but it is an ordered pipeline with no aggregate reply, so a command may still
        /// inject a connection-local preamble there. Inside <c>MULTI</c>/<c>EXEC</c> it may not: an extra
        /// frame lands in the <c>EXEC</c> array and desyncs every result after it, which is the same
        /// reason <c>SELECT</c>-injection is forbidden there.
        /// <para>
        /// Asked rather than type-tested, as the other capabilities are. The shipped core spells this as a
        /// hand-written <c>this is ITransaction</c> in each affected method - four of them, each somewhere
        /// different, which <c>MultiMessageInTransactionTests</c> describes as "the safety is someone
        /// remembered, four times".
        /// </para>
        /// </remarks>
        internal virtual bool Transactional => false;

        /// <summary>
        /// Write a <b>preamble</b> immediately before a request, on the same connection and with nothing
        /// interleaved.
        /// </summary>
        /// <param name="preamble">The conditional first frame.</param>
        /// <param name="request">The request whose reply the caller wants.</param>
        /// <param name="gate">Decides at write time whether the preamble is still needed.</param>
        /// <param name="cancellationToken">Cancels the send.</param>
        /// <remarks>
        /// <para>
        /// Optional: an executor that does not offer it still works, because the caller sends the two in
        /// sequence and waits for the first. That is a round trip worse and semantically identical, which
        /// is the right trade for a fake in a test - nothing has to be updated for a capability it does
        /// not need.
        /// </para>
        /// <para>
        /// The motivating case is <c>SCRIPT LOAD</c> before <c>EVALSHA</c>. Note what is <b>not</b> being
        /// asked for: the two are separate frames, each a pure function of its arguments, so the request
        /// keeps its identity as a cache key and its routing. Only their adjacency is being requested.
        /// </para>
        /// </remarks>
        public virtual ValueTask<RespPayload> SendAsync(
            RespRequest preamble,
            RespRequest request,
            IRespPreambleGate? gate,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException($"{GetType().Name} cannot write a preamble.");
    }

    /// <summary>
    /// A handler whose result <b>retains</b> the reply's buffer, and therefore needs the payload itself
    /// rather than a view of its bytes.
    /// </summary>
    /// <typeparam name="TResult">What parsing the reply produces.</typeparam>
    /// <remarks>
    /// <para>
    /// <b>Internal, deliberately.</b> A <see cref="ReadOnlySpan{T}"/> cannot carry buffer identity, so a
    /// handler given one can only ever copy - which is right for the general case and for anything supplied
    /// from outside. Retaining the buffer instead is safe only for a result that cannot write through it,
    /// and judging that is a privilege the library keeps rather than an option it offers.
    /// </para>
    /// <para>
    /// Implementations must take their own reference; the pipeline releases its own as soon as parsing
    /// returns.
    /// </para>
    /// </remarks>
    internal interface IRespPayloadHandler<TResult> : IRespHandler<TResult>
    {
        /// <summary>Parse the reply, optionally retaining its buffer.</summary>
        /// <param name="payload">The reply; take a reference if the result outlives this call.</param>
        TResult Parse(RespPayload payload);
    }

    /// <summary>
    /// Sending a request, with or without a client-side cache.
    /// </summary>
    /// <remarks>
    /// The cache is an <b>optional participant in the send</b>, not the entry point. The call site is
    /// identical whether or not caching is configured, so enabling it does not mean rewriting callers, and
    /// "no cache" is an ordinary case rather than a missing one. It is also the right layering: a cache that
    /// called the executor would have to sit above dispatch and know how to send.
    /// </remarks>
    // RS0027 wants the overload carrying optional parameters to have the most parameters. It is guidance
    // aimed at ambiguity when parameters are added later, and it does not apply here: the two overloads
    // differ in the TYPE of their second parameter - an interpolated-string handler versus a rendered
    // frame - so no call can be ambiguous between them, whatever is added. Both are public because the
    // frame form is what Compose produces, and that path is public.
    [SuppressMessage("ApiDesign", "RS0027:API with optional parameter(s) should have the most parameters amongst its public overloads", Justification = "Overloads differ by parameter type; ambiguity is impossible")]

    // RS0026 warns that overloads carrying optional parameters can become ambiguous when a parameter is
    // added later. Not here: the SendAsync overloads are told apart by their FIRST parameter - a rendered
    // RespRequestFrame, an interpolated RespRequestBuilder, a string command - and that parameter never
    // has a default, so a call can only ever bind to one of them however many optionals arrive after it.
    [SuppressMessage("ApiDesign", "RS0026:Do not add multiple overloads with optional parameters", Justification = "Overloads differ in a leading parameter that has no default; see the comment above")]
    public static class RespExecutor
    {
        /// <summary>
        /// Parse a reply, or produce the default when there was none.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Fire-and-forget has no reply at all.</b> The caller has explicitly declined it, so the
        /// pipeline never captures one and the executor hands back <see langword="null"/> - which is not an
        /// error, and is why every path that reaches a handler goes through here rather than dereferencing
        /// the payload. <c>default</c> is what the existing surface has always returned for such a call
        /// (<c>ExecuteSync</c>/<c>ExecuteAsync</c> return <c>default(T)</c>), so the two agree.
        /// </para>
        /// <para>
        /// Stated once, on purpose: there are five places a reply reaches a handler, and the cost of one of
        /// them forgetting this is a <see cref="NullReferenceException"/> from inside an <c>await</c>,
        /// which says nothing about fire-and-forget to whoever has to read it.
        /// </para>
        /// </remarks>
        /// <summary>
        /// Send a request preceded by a preamble that must reach the same connection, immediately before it.
        /// </summary>
        /// <typeparam name="TResult">What parsing the request's reply produces.</typeparam>
        /// <param name="context">The context to send through.</param>
        /// <param name="preamble">Written first; its reply is consumed and discarded.</param>
        /// <param name="request">The request whose reply the caller wants.</param>
        /// <param name="flags">The request's flags.</param>
        /// <param name="handler">Turns the request's reply into a result.</param>
        /// <param name="gate">If set, decides at write time whether the preamble is still needed.</param>
        /// <param name="cancellationToken">Reserved; must not be cancellable yet.</param>
        /// <remarks>
        /// <para>
        /// Bypasses the cache entirely: the only user so far is a script evaluation, and a preamble exists
        /// precisely because the request cannot stand alone - so serving the request from cache would skip
        /// the very thing the preamble was for. When a read-only script becomes cacheable this will need
        /// revisiting, and the answer will be to cache the <i>request</i> frame alone, never the pair.
        /// </para>
        /// <para>
        /// An executor that cannot write the two as a unit sends them in sequence instead - correct, one
        /// round trip worse, and the reason test fakes need no changes.
        /// </para>
        /// </remarks>
        internal static ValueTask<TResult> SendWithPreambleAsync<TResult>(
            this RespContext context,
            ref RespRequestFrame preamble,
            ref RespRequestFrame request,
            CommandFlags flags,
            IRespHandler<TResult> handler,
            IRespPreambleGate? gate = null,
            CancellationToken cancellationToken = default)
        {
            if (handler is null) throw new ArgumentNullException(nameof(handler));
            flags = flags.WithDefaultCategory(request.Command); // see the note in SendAsync
            var executor = context.Executor ?? throw new InvalidOperationException("No executor is configured for this context.");

            // detach HERE, not in the async continuation. RespRequestFrame is a struct, so a by-value parameter
            // would hand the continuation a copy: Detach would empty the copy, the caller's frame would
            // still hold the buffer, and disposing it would return an array that is still being written -
            // which shows up as somebody else's reply arriving for your command. Taking them by ref means
            // the caller's frames really are emptied, and their Dispose is the no-op it looks like.
            var head = preamble.Detach(CommandFlags.CommandRetryAlways);
            var body = request.Detach(flags);
            return AwaitPair(executor, head, body, gate, handler, cancellationToken);
        }

        /// <summary>
        /// As above, for a preamble that is <b>already detached</b> and owns nothing poolable.
        /// </summary>
        /// <typeparam name="TResult">What parsing the request's reply produces.</typeparam>
        /// <param name="context">The context to send through.</param>
        /// <param name="preamble">A request over a fixed buffer - a cached rendering, typically.</param>
        /// <param name="request">The request whose reply the caller wants.</param>
        /// <param name="flags">The request's flags.</param>
        /// <param name="handler">Turns the request's reply into a result.</param>
        /// <param name="gate">If set, decides at write time whether the preamble is still needed.</param>
        /// <param name="cancellationToken">Reserved; must not be cancellable yet.</param>
        /// <remarks>
        /// No <c>ref</c> on the preamble, and no ownership question either: it is a fixed buffer that is
        /// never returned to a pool, so the release at the end of the send is a no-op rather than a
        /// hand-back. That is the whole reason a cached rendering can be shared by concurrent calls.
        /// </remarks>
        internal static ValueTask<TResult> SendWithPreambleAsync<TResult>(
            this RespContext context,
            RespRequest preamble,
            ref RespRequestFrame request,
            CommandFlags flags,
            IRespHandler<TResult> handler,
            IRespPreambleGate? gate = null,
            CancellationToken cancellationToken = default)
        {
            if (handler is null) throw new ArgumentNullException(nameof(handler));
            flags = flags.WithDefaultCategory(request.Command); // see the note in SendAsync
            var executor = context.Executor ?? throw new InvalidOperationException("No executor is configured for this context.");

            var body = request.Detach(flags);
            return AwaitPair(executor, preamble, body, gate, handler, cancellationToken);
        }

#if NET6_0_OR_GREATER
        [AsyncMethodBuilder(typeof(System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder<>))]
#endif
        private static async ValueTask<TResult> AwaitPair<TResult>(
            RespExecutorBase executor,
            RespRequest head,
            RespRequest body,
            IRespPreambleGate? gate,
            IRespHandler<TResult> handler,
            CancellationToken cancellationToken)
        {
            try
            {
                RespPayload? response;
                if (executor.CanWritePreamble)
                {
                    response = await executor.SendAsync(head, body, gate, cancellationToken).ForAwait();
                }
                else if (executor.Accumulates)
                {
                    // The sequential fallback below cannot work here, and would not fail - it would HANG.
                    // Nothing is sent until the run is, so awaiting the preamble waits for a send that this
                    // very call is holding up; see the remarks on Accumulates, which predict exactly this.
                    // The shipped core refuses the same shape up front, in QueuedMessage's constructor, and
                    // this is that refusal for the context surface.
                    throw new NotSupportedException(
                        "This command is not supported inside a transaction or batch: it expands into several "
                        + "messages, and only the outer one would be written - every extra message would add a "
                        + "slot to the positional EXEC result array.");
                }
                else
                {
                    // sequential fallback: wait for the preamble, then send. Ordering is what matters, and
                    // awaiting gives it - at the cost of the round trip a unit would have saved. The gate is
                    // not CONSULTED here - it asks about a connection, and the decision has already been
                    // made by the time this path is chosen - but it is told, which is a different question;
                    // see SendPreambleAsync.
                    await executor.SendPreambleAsync(head, gate, cancellationToken).ForAwait();
                    response = await executor.SendAsync(body, cancellationToken).ForAwait();
                }

                try
                {
                    return Parse(handler, response);
                }
                finally
                {
                    response?.Release();
                }
            }
            finally
            {
                head.Dispose();
                body.Dispose();
            }
        }

        /// <summary>
        /// Whether this command changes keys, so far as its flags admit.
        /// </summary>
        /// <remarks>
        /// The retry category is a severity ladder; anything past
        /// <see cref="CommandFlags.CommandRetryReadOnly"/> writes. <b>An undeclared category counts as a
        /// write too</b>, which is the same judgement the caching side makes from the other direction:
        /// undeclared cannot mean safe, so there it means "do not cache" and here it means "assume it
        /// wrote". Both err towards a miss.
        /// </remarks>
        private static bool Mutates(CommandFlags flags)
        {
            var category = flags & CommandFlagsInternal.MaskRetryCategory;
            return category == 0 || category > CommandFlags.CommandRetryReadOnly;
        }

        /// <summary>
        /// Tell the cache about a write of ours <b>before it goes out</b>, so a read cannot slip between the
        /// send and the server's echo of it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Server-assisted invalidation cannot do this job. Measured against a real server, a write's own
        /// invalidation arrives <i>after</i> its reply - and after the replies of anything pipelined behind
        /// it - so <c>SET k v</c> followed by <c>GET k</c> returns the read before the notice about the
        /// write. Without this, that read is answered from a stale entry and corrected afterwards, which is
        /// the one kind of staleness this design refuses: handing a caller back the value they just
        /// replaced. See design notes 6.13.
        /// </para>
        /// <para>
        /// Outside the "may I cache this?" gate, because a write is exactly what that gate excludes - which
        /// is why nothing called <c>OnLocalWrite</c> until now.
        /// </para>
        /// </remarks>
        private static void NoteLocalWrite(RespClientCache? cache, in RespRequestFrame request, CommandFlags flags)
        {
            if (cache is not null && Mutates(flags)) cache.OnLocalWrite(request.AsLookupKey());
        }

        /// <summary>Position a reader on the reply's first element and hand it to the handler.</summary>
        /// <typeparam name="TResult">What parsing the reply produces.</typeparam>
        /// <param name="handler">Turns the reply into a result.</param>
        /// <param name="response">The reply bytes.</param>
        /// <remarks>
        /// The two lines every handler used to open with, in the one place every reply funnels through.
        /// </remarks>
        internal static TResult ParseFromSpan<TResult>(IRespHandler<TResult> handler, ReadOnlySpan<byte> response)
        {
            var reader = new RespReader(response);
            reader.MoveNext();
            return handler.Parse(ref reader);
        }

        /// <summary>
        /// Refuse a token we cannot honour, rather than accepting one and ignoring it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Cancellation <b>will</b> be supported; it is not yet. The pipeline beneath this has no notion of
        /// it, so a token that could actually fire would be silently inert - a promise in the signature that
        /// nothing keeps. <c>default</c> and <see cref="CancellationToken.None"/> cost nothing and pass
        /// through; anything cancellable says so here, at the call that would have relied on it.
        /// </para>
        /// <para>
        /// <b>Except the half that already works.</b> A token cancelled <i>before</i> the call is honoured
        /// properly - we cannot stop an in-flight request, but we can decline to start one - so that gets an
        /// <see cref="OperationCanceledException"/>, not "not implemented". It is checked first, since a
        /// cancelled token is also a cancellable one.
        /// </para>
        /// </remarks>
        /// <summary>
        /// As above, for the interpolated forms: the handler has already rented a buffer by the time we are
        /// called, so refusing has to hand it back.
        /// </summary>
        /// <remarks>
        /// The buffer is rented in the CALLER's frame, before this method is entered - the same reason the
        /// context's old cancellation check disposed the handler rather than simply throwing.
        /// </remarks>
        /// <summary>Turn a reply that says we are not authenticated into the connection failure it is.</summary>
        /// <param name="executor">The executor that carried the command, for somewhere to report to.</param>
        /// <param name="ex">The server's error.</param>
        /// <returns>The exception to throw instead, or null to let the server's error stand.</returns>
        /// <remarks>
        /// <b><c>NOAUTH</c> is a statement about the connection, not about the command.</b> A caller who
        /// gets "NOAUTH Authentication required" back from a <c>PING</c> has not written a bad <c>PING</c>;
        /// their credentials are wrong, and the useful error says so and says which knob to turn. Shipped
        /// does this in <c>ResultProcessor</c>'s common error handling, recording the suspicion against the
        /// multiplexer so that <c>ExceptionFactory.UnableToConnect</c> can explain it - and this core passed
        /// the server's words through untouched, so the diagnosis was left to the reader.
        /// <para>
        /// The synthesised wording for <c>NOAUTH</c> is shipped's, deliberately: it is what
        /// <c>SecureTests.ConnectWithWrongPassword</c> reads, and more to the point it is better than the
        /// server's own - "connection has not yet authenticated" names the cause where "authentication
        /// required" only names the symptom. A <c>WRONGPASS</c> keeps the server's message, which already
        /// says precisely what is wrong.
        /// </para>
        /// <para>
        /// <c>SetAuthSuspect</c> keeps the FIRST report, so a refusal already recorded during the handshake
        /// wins over this one - which is what makes the wrong-password case read as <c>WRONGPASS</c> rather
        /// than as the <c>NOAUTH</c> that followed it.
        /// </para>
        /// </remarks>
        internal static Exception? AuthFault(RespExecutorBase executor, RedisServerException ex)
        {
            if (ex.Kind is not (RedisErrorKind.NoAuth or RedisErrorKind.WrongPass)) return null;
            if (executor.Multiplexer is not { } muxer) return null;

            muxer.SetAuthSuspect(ex.Kind == RedisErrorKind.NoAuth
                ? new RedisServerException(
                    ex.Kind, CommandFlags.None, "NOAUTH Returned - connection has not yet authenticated")
                : ex);

            return ExceptionFactory.UnableToConnect(muxer);
        }

        private static void DemandCancellable(RespContext context, ref RespRequestBuilder request, CancellationToken cancellationToken)
        {
            // already cancelled is the half we CAN honour - refusing to start costs nothing - so it gets the
            // right exception rather than "not implemented". Checked first, because a cancelled token is
            // also a cancellable one.
            if (cancellationToken.IsCancellationRequested)
            {
                request.Dispose();
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (cancellationToken.CanBeCanceled && context.Executor is not { CanCancel: true })
            {
                request.Dispose();
                ThrowCannotCancel();
            }
        }

        /// <summary>Refuse a cancellable token unless the executor beneath can act on one.</summary>
        /// <remarks>
        /// <b>A capability, not a blanket refusal.</b> This used to say no to every cancellable token,
        /// which was right when the only executor was the <c>Message</c> shim: that pipeline cannot
        /// withdraw a request once it has reached the socket, and honouring a token there would mean
        /// abandoning the caller while the command still runs on the server. That is not cancellation, it
        /// is a lie about cancellation, and the wrong one to tell. The new core's operation genuinely
        /// takes the outcome first - a cancellation competes for the same single-winner claim a reply
        /// does - so it answers yes and the refusal stops applying to it.
        /// </remarks>
        private static void DemandCancellable(RespContext context, CancellationToken cancellationToken)
        {
            // cancelled-before-we-started is honoured whatever the executor says, because it can be:
            // refusing to start costs nothing. Checked first, since a cancelled token is also cancellable.
            cancellationToken.ThrowIfCancellationRequested();

            if (cancellationToken.CanBeCanceled && context.Executor is not { CanCancel: true })
            {
                ThrowCannotCancel();
            }
        }

        private static void ThrowCannotCancel()
            => throw new NotImplementedException(
                "Cancellation is not supported by this executor: the underlying pipeline cannot cancel an "
                + "in-flight request, so honouring the token is not possible. Pass 'default', or use a "
                + "context whose executor supports cancellation.");

        internal static TResult Parse<TResult>(IRespHandler<TResult> handler, RespPayload? response)
            => response switch
            {
                null => default!,

                // a handler whose result retains the buffer needs the payload, not a view of it; one test,
                // in the one place every reply already funnels through
                _ when handler is IRespPayloadHandler<TResult> retaining => retaining.Parse(response),

                _ => ParseFromSpan(handler, response.Span),
            };

        /// <summary>
        /// Send a request and parse the reply, optionally serving it from - and populating - the context's cache.
        /// </summary>
        /// <typeparam name="TResult">What parsing the reply produces.</typeparam>
        /// <param name="context">The context to send through; supplies the executor, cache and cancellation.</param>
        /// <param name="request">The rendered request; consumed by this call on every path.</param>
        /// <param name="flags">
        /// The command's flags. Caching additionally requires a declared retry category no more severe than
        /// <see cref="CommandFlags.CommandRetryReadOnly"/>; see
        /// <see cref="RespClientCache.TryBeginFill(ref RespRequestFrame, int, CommandFlags, out RespClientCache.RespFill)"/>.
        /// </param>
        /// <param name="handler">Turns the reply into a result.</param>
        /// <remarks>
        /// <para>
        /// <paramref name="flags"/> is deliberately <b>not</b> optional. Every <c>IDatabase</c> method in
        /// this library already carries flags, and whether a command may be cached is a property of the
        /// command, not of the call site's enthusiasm - so the caller has to say. Saying nothing
        /// (<see cref="CommandFlags.None"/>) means no caching, which is the safe reading for any command
        /// this library does not itself define.
        /// </para>
        /// Three lifetimes are handled here so that no caller has to: the key generations are captured
        /// <b>before</b> the send; the payload is retained across <see cref="IRespHandler{TResult}.Parse"/>
        /// and released in a <c>finally</c>; and the request is consumed on every path.
        /// </remarks>
        /// <param name="cancellationToken">Reserved; must not be cancellable yet.</param>
        public static TResult Send<TResult>(
            this RespContext context,
            ref RespRequestFrame request,
            CommandFlags flags,
            IRespHandler<TResult> handler,
            CancellationToken cancellationToken)
        {
            if (handler is null) throw new ArgumentNullException(nameof(handler));
            DemandCancellable(context, cancellationToken);

            // THE one place a command's retry category is applied. The frame already carries the
            // RedisCommand it rendered - stored when the command hole was written, not re-parsed - so no
            // call site has to remember, and a new command cannot silently arrive without one. It is
            // WithDefaultCategory rather than an override, so the 18 sites whose ARGUMENTS change the
            // answer (SORT with STORE, GETEX with a TTL) still raise themselves first and win.
            //
            // It must happen HERE rather than at the call site for an ordering reason as well as a
            // forgetting one: the cache reads flags to decide whether a reply may be served or stored, so
            // the category has to be settled before PermitsCaching sees it. At the call site that was
            // convention; here it is the order of the statements.
            flags = flags.WithDefaultCategory(request.Command);

            var executor = context.Executor ?? ThrowNoExecutor(ref request);
            var cache = context.Cache;
            NoteLocalWrite(cache, in request, flags);

            // NoClientCache suppresses the PROBE as well as the store: opting out must mean the caller does
            // not get a cached answer either, not merely that this reply is not kept
            if (cache is not null && cache.PermitsCaching(flags))
            {
                if (TryServeFromCache(executor, ref request, handler, cache, context.MaxCacheAgeTicks, flags, out var cached)) return cached;

                // NOTE: no in-flight wait here. Coalescing means waiting on someone else's Task, and doing
                // that from a synchronous caller is the sync-over-async problem this design avoids
                // elsewhere; sync callers therefore still send their own copy, exactly as before. Sync is
                // deprioritised (see RedisDatabase), so this is a deliberate gap rather than an
                // oversight - it closes when the executor gains a synchronous wait.
                if (cache.TryBeginFill(ref request, executor.Database, flags, out var fill))
                {
                    RespPayload filled;
                    try
                    {
                        // generations captured above, BEFORE this send
                        filled = executor.Send(fill.Key);
                    }
                    catch
                    {
                        fill.Abandon(); // release any waiters, and the key
                        throw;
                    }

                    try
                    {
                        if (filled is null)
                        {
                            // no reply is coming, so nothing can fill this. Fire-and-forget used to arrive
                            // here; it is now refused by the flags before a fill is ever begun, because a
                            // cache HIT on one would have returned a value where the contract says default.
                            // Kept for an executor that answers null for some other reason of its own.
                            fill.Abandon(); // release any waiters, and the key
                            return default!;
                        }

                        cache.TryComplete(fill, filled);
                        return Parse(handler, filled);
                    }
                    finally
                    {
                        filled?.Release();
                    }
                }

                // not cacheable, which is precisely the uncached case - fall through to it
            }

            // the executor may need the bytes past this call, so hand it something it can retain
            var owned = request.Detach(flags);
            try
            {
                var response = executor.Send(owned);
                try
                {
                    return Parse(handler, response);
                }
                finally
                {
                    response?.Release();
                }
            }
            finally
            {
                owned.Dispose();
            }
        }

        /// <inheritdoc cref="Send{TResult}(RespContext, ref RespRequestFrame, CommandFlags, IRespHandler{TResult}, CancellationToken)"/>
        /// <param name="context">The context to send through; supplies the executor, cache and cancellation.</param>
        /// <param name="request">The rendered request; consumed by this call on every path.</param>
        /// <param name="handler">Turns the reply into a result.</param>
        /// <param name="flags">The command's flags; see the synchronous overload.</param>
        /// <remarks>
        /// Deliberately <b>not</b> an <c>async</c> method: <c>async</c> forbids <c>ref</c> parameters, and
        /// the frame has to be consumed by reference so the caller's copy cannot be used or disposed twice.
        /// So the probe and the hand-off happen synchronously here, and only the awaiting tail is a separate
        /// <c>async</c> method. A cache hit therefore completes synchronously and allocates nothing - no
        /// state machine, no <c>Task</c>.
        /// </remarks>
        /// <param name="cancellationToken">Reserved; must not be cancellable yet.</param>
        public static ValueTask<TResult> SendAsync<TResult>(
            this RespContext context,
            ref RespRequestFrame request,
            CommandFlags flags,
            IRespHandler<TResult>? handler = null,
            CancellationToken cancellationToken = default)
        {
            // The handler defaults exactly as it does on the interpolated overload - omitting it asks for
            // the inbuilt handler for TResult - so a command factory does not force its callers to spell
            // out a handler they were happy to leave implicit before the factory existed.
            handler ??= RespHandlers.Inbuilt<TResult>.Require();
            DemandCancellable(context, cancellationToken);

            // THE one place a command's retry category is applied. The frame already carries the
            // RedisCommand it rendered - stored when the command hole was written, not re-parsed - so no
            // call site has to remember, and a new command cannot silently arrive without one. It is
            // WithDefaultCategory rather than an override, so the 18 sites whose ARGUMENTS change the
            // answer (SORT with STORE, GETEX with a TTL) still raise themselves first and win.
            //
            // It must happen HERE rather than at the call site for an ordering reason as well as a
            // forgetting one: the cache reads flags to decide whether a reply may be served or stored, so
            // the category has to be settled before PermitsCaching sees it. At the call site that was
            // convention; here it is the order of the statements.
            flags = flags.WithDefaultCategory(request.Command);

            var executor = context.Executor ?? ThrowNoExecutor(ref request);
            var cache = context.Cache;
            NoteLocalWrite(cache, in request, flags);

            if (cache is not null && cache.PermitsCaching(flags))
            {
                if (TryServeFromCache(executor, ref request, handler, cache, context.MaxCacheAgeTicks, flags, out var cached))
                {
                    return new ValueTask<TResult>(cached);
                }

                // somebody is already fetching exactly this - wait for them instead of sending a second
                // copy. See design notes 6.15; this is the whole of the stampede fix at the call site.
                if (cache.TryAwaitInFlight(request.AsLookupKey(), executor.Database, out var pending))
                {
                    return AwaitShared(executor, request.Detach(flags), pending, handler, cache, context.MaxCacheAgeTicks, cancellationToken);
                }

                if (cache.TryBeginFill(ref request, executor.Database, flags, out var fill))
                {
                    return AwaitFill(executor, fill, handler, cache, cancellationToken);
                }
            }

            // An executor that copies the bytes during the call is handed a view of the frame, and the frame's
            // array goes back to the pool here, on the thread that rented it: no lease, and nothing of the
            // caller's kept for the round trip. Everything else gets the lease, as before.
            if (executor.CopiesRequestOnSend && typeof(TResult) != typeof(RespPayload))
            {
                try
                {
                    return executor.SendTypedAsync(request.AsLookupKey(flags), handler, cancellationToken);
                }
                finally
                {
                    request.Dispose();
                }
            }

            return executor.SendTypedAsync(request.Detach(flags), handler, cancellationToken);
        }

        /// <summary>
        /// Compose and send in one expression: <c>ctx.SendAsync&lt;RedisValue&gt;($"{cmd}{key}", flags)</c>.
        /// </summary>
        /// <typeparam name="TResult">What parsing the reply produces.</typeparam>
        /// <param name="context">The context to send through.</param>
        /// <param name="request">The command, written as an interpolated string.</param>
        /// <param name="flags">The command's flags.</param>
        /// <param name="handler">
        /// Turns the reply into a result; omit it to use the built-in handler for <typeparamref name="TResult"/>.
        /// </param>
        /// <remarks>
        /// <para>
        /// The <c>ref</c> is implied: the compiler builds the handler from the interpolated string and
        /// passes it by reference, exactly as <c>RespContext.Execute</c> already does. So a whole command
        /// is one expression, which is the point of the surface.
        /// </para>
        /// <para>
        /// <b>Flags come before the handler</b> so the handler can be omitted. <typeparamref name="TResult"/>
        /// must then be given explicitly - C# does not infer type arguments from a return type - which is
        /// why this reads <c>SendAsync&lt;RedisValue&gt;</c> rather than inferring it.
        /// </para>
        /// </remarks>
        /// <param name="cancellationToken">Reserved; must not be cancellable yet.</param>
        public static ValueTask<TResult> SendAsync<TResult>(
            this RespContext context,
            [InterpolatedStringHandlerArgument(nameof(context))] ref RespRequestBuilder request,
            CommandFlags flags,
            IRespHandler<TResult>? handler = null,
            CancellationToken cancellationToken = default)
        {
            DemandCancellable(context, ref request, cancellationToken);
            var frame = request.Complete();
            return SendAsync(context, ref frame, flags, handler ?? RespHandlers.Inbuilt<TResult>.Require(), cancellationToken);
        }

        /// <summary>
        /// Compose and send a command whose reply carries nothing worth reading:
        /// <c>await ctx.SendAsync($"{cmd}{key}", flags)</c>.
        /// </summary>
        /// <param name="context">The context to send through.</param>
        /// <param name="request">The command, written as an interpolated string.</param>
        /// <param name="flags">The command's flags.</param>
        /// <remarks>
        /// <para>
        /// The result-less form removes the last piece of ceremony from a command that has no result:
        /// there is no <c>TResult</c> to name, so there is no type argument, and a command body is just
        /// <c>=&gt; ctx.SendAsync($"...", flags);</c>.
        /// </para>
        /// <para>
        /// <b>It still reads the reply</b> - via <see cref="RespHandlers.Success"/> - because a server
        /// error is the only thing a call with no return value can report, and discarding the reply
        /// wholesale would discard that too.
        /// </para>
        /// <para>
        /// No ambiguity with the generic overloads: a result type cannot be inferred from a return type,
        /// so an un-annotated call can only bind here, and a <c>SendAsync&lt;T&gt;</c> call can only bind
        /// there.
        /// </para>
        /// <para>
        /// A synchronously-completed send - notably a cache hit - returns a default
        /// <see cref="ValueTask"/> and allocates nothing, which is the same promise the generic overload
        /// makes and would be lost by simply awaiting it in an <c>async</c> wrapper.
        /// </para>
        /// </remarks>
        /// <param name="cancellationToken">Cancels the request: one not yet written is never sent; one already written still runs on the server, and its reply is discarded.</param>
        public static ValueTask SendAsync(
            this RespContext context,
            [InterpolatedStringHandlerArgument(nameof(context))] ref RespRequestBuilder request,
            CommandFlags flags = CommandFlags.None,
            CancellationToken cancellationToken = default)
        {
            DemandCancellable(context, ref request, cancellationToken);
            var frame = request.Complete();

            // the direct path, as the typed one takes for an uncached send: the operation is the awaitable, so
            // no adapter boxes per command. Only without a cache - a cached send has its own machinery - and
            // only where the executor copies the request during the call, so the frame may be lent.
            if (context.Cache is null && context.Executor is { CopiesRequestOnSend: true } executor)
            {
                flags = flags.WithDefaultCategory(frame.Command);
                try
                {
                    return executor.SendVoidAsync(frame.AsLookupKey(flags), RespHandlers.Success, cancellationToken);
                }
                finally
                {
                    frame.Dispose();
                }
            }

            var pending = SendAsync(context, ref frame, flags, RespHandlers.Success, cancellationToken);
            return pending.IsCompletedSuccessfully ? default : Awaited(pending);

            static async ValueTask Awaited(ValueTask<bool> pending) => await pending.ConfigureAwait(false);
        }

        /// <summary>Compose and send, from a typed context.</summary>
        /// <typeparam name="TResult">The type the reply is read as.</typeparam>
        /// <param name="context">The context to send through.</param>
        /// <param name="request">The command, written as an interpolated string.</param>
        /// <param name="flags">The command's flags.</param>
        /// <param name="handler">Reads the reply; the inbuilt handler for <typeparamref name="TResult"/> when omitted.</param>
        /// <param name="cancellationToken">Cancels the request: one not yet written is never sent; one already written still runs on the server, and its reply is discarded.</param>
        /// <remarks>
        /// One line each, forwarding to the <see cref="RespContext"/> implementation. They exist because a
        /// typed context is now the thing callers hold, and an interpolated-string handler is built from
        /// the receiver - so without these the send path would have to be written out per context instead
        /// of being one implementation with two doors.
        /// </remarks>
        public static ValueTask<TResult> SendAsync<TResult>(
            this RespDatabaseContext context,
            [InterpolatedStringHandlerArgument(nameof(context))] ref RespRequestBuilder request,
            CommandFlags flags = CommandFlags.None,
            IRespHandler<TResult>? handler = null,
            CancellationToken cancellationToken = default)
            => SendAsync(context.Raw, ref request, flags, handler, cancellationToken);

        /// <summary>Compose and send a command whose reply carries nothing worth reading, from a typed context.</summary>
        /// <param name="context">The context to send through.</param>
        /// <param name="request">The command, written as an interpolated string.</param>
        /// <param name="flags">The command's flags.</param>
        /// <param name="cancellationToken">Cancels the request: one not yet written is never sent; one already written still runs on the server, and its reply is discarded.</param>
        public static ValueTask SendAsync(
            this RespDatabaseContext context,
            [InterpolatedStringHandlerArgument(nameof(context))] ref RespRequestBuilder request,
            CommandFlags flags = CommandFlags.None,
            CancellationToken cancellationToken = default)
            => SendAsync(context.Raw, ref request, flags, cancellationToken);

        /// <summary>Compose and send, from a server context.</summary>
        /// <typeparam name="TResult">The type the reply is read as.</typeparam>
        /// <param name="context">The context to send through.</param>
        /// <param name="request">The command, written as an interpolated string.</param>
        /// <param name="flags">The command's flags.</param>
        /// <param name="handler">Reads the reply; the inbuilt handler for <typeparamref name="TResult"/> when omitted.</param>
        /// <param name="cancellationToken">Cancels the request: one not yet written is never sent; one already written still runs on the server, and its reply is discarded.</param>
        public static ValueTask<TResult> SendAsync<TResult>(
            this RespServerContext context,
            [InterpolatedStringHandlerArgument(nameof(context))] ref RespRequestBuilder request,
            CommandFlags flags = CommandFlags.None,
            IRespHandler<TResult>? handler = null,
            CancellationToken cancellationToken = default)
            => SendAsync(context.Raw, ref request, flags, handler, cancellationToken);

        /// <inheritdoc cref="SendAsync{TResult}(RespContext, ref RespRequestBuilder, CommandFlags, IRespHandler{TResult}, CancellationToken)"/>
        /// <param name="context">The context to send through.</param>
        /// <param name="request">The command, written as an interpolated string.</param>
        /// <param name="flags">The command's flags.</param>
        /// <param name="handler">Turns the reply into a result; omit for the built-in one.</param>
        /// <param name="cancellationToken">Reserved; must not be cancellable yet.</param>
        public static TResult Send<TResult>(
            this RespContext context,
            [InterpolatedStringHandlerArgument(nameof(context))] ref RespRequestBuilder request,
            CommandFlags flags,
            IRespHandler<TResult>? handler = null,
            CancellationToken cancellationToken = default)
        {
            DemandCancellable(context, ref request, cancellationToken);
            var frame = request.Complete();
            return Send(context, ref frame, flags, handler ?? RespHandlers.Inbuilt<TResult>.Require(), cancellationToken);
        }

        [DoesNotReturn]
        private static RespExecutorBase ThrowNoExecutor(ref RespRequestFrame request)
        {
            request.Dispose();
            throw new InvalidOperationException("No executor is configured on this context.");
        }

        // the cache probe is identical for both, and borrows rather than detaching: on a HIT the request
        // never reaches the executor, so it never needs an owned lease
        private static bool TryServeFromCache<TResult>(
            RespExecutorBase executor,
            ref RespRequestFrame request,
            IRespHandler<TResult> handler,
            RespClientCache cache,
            long maxAgeTicks,
            CommandFlags flags,
            [MaybeNullWhen(false)] out TResult result)
        {
            if (!cache.TryGet(request.AsLookupKey(), executor.Database, maxAgeTicks, out var hit, out var refresh))
            {
                result = default;
                return false;
            }

            if (refresh)
            {
                // this caller claimed the refresh, so the request cannot simply be dropped: it IS the thing
                // that needs re-sending. Detach hands ownership to the background send, which disposes it.
                StartRefresh(executor, request.Detach(flags), cache);
            }
            else
            {
                request.Dispose();
            }

            try
            {
                result = Parse(handler, hit);
                return true;
            }
            finally
            {
                hit.Release();
            }
        }

        /// <summary>
        /// Re-fetch an ageing entry in the background, while its old value is still being served.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>No factory, no captured state.</b> Refreshing means re-sending the request, because the cache
        /// key <i>is</i> the request - so this needs nothing from the caller and retains nothing of theirs.
        /// It is also handler-agnostic: the cache stores the raw reply, so a refresh does not need to know
        /// what anybody intended to turn the bytes into.
        /// </para>
        /// <para>
        /// Deliberately not awaited. The caller already has an answer - that is the whole point of serving
        /// stale - so the refresh must not make them wait for a better one. Which means nothing observes the
        /// task, and every failure has to be swallowed here: an unobserved faulted task is a process-level
        /// event, and a refresh failing is a normal occurrence rather than an error.
        /// </para>
        /// <para>
        /// The claim is handed back on <b>every</b> path. A refresh that throws and keeps its claim would
        /// pin the entry stale until its hard expiry, still serving the whole time.
        /// </para>
        /// </remarks>
        private static void StartRefresh(
            RespExecutorBase executor,
            RespRequest request,
            RespClientCache cache)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    if (!cache.TryBeginRefresh(request, executor.Database, out var fill))
                    {
                        return;
                    }

                    RespPayload? response = null;
                    try
                    {
                        response = await executor.SendAsync(fill.Key).ConfigureAwait(false);
                        if (response is not null) cache.TryComplete(fill, response);
                        else fill.Abandon();
                    }
                    catch
                    {
                        fill.Abandon();
                        throw;
                    }
                    finally
                    {
                        response?.Release();
                    }
                }
                catch
                {
                    // a refresh is best-effort by construction: the caller already has an answer, and the
                    // entry expires on its own if this keeps failing
                }
                finally
                {
                    cache.EndRefresh(request, executor.Database);
                    request.Dispose();
                }
            });
        }

#if NET6_0_OR_GREATER
        [AsyncMethodBuilder(typeof(System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder<>))]
#endif
        private static async ValueTask<TResult> AwaitFill<TResult>(
            RespExecutorBase executor,
            RespClientCache.RespFill fill,
            IRespHandler<TResult> handler,
            RespClientCache cache,
            CancellationToken cancellationToken)
        {
            RespPayload response;
            try
            {
                response = await executor.SendAsync(fill.Key, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // a fill that never completes strands its waiters on a reply that is never coming, and
                // leaks the key; Abandon does both halves
                fill.Abandon();
                throw;
            }

            try
            {
                if (response is null)
                {
                    // as in the synchronous path: fire-and-forget no longer gets this far, but an executor
                    // may still answer null, and a fill left open would strand its waiters
                    fill.Abandon();
                    return default!;
                }

                cache.TryComplete(fill, response);
                return Parse(handler, response);
            }
            finally
            {
                response?.Release();
            }
        }

        /// <summary>
        /// Wait for a request already in flight, then take the answer from the cache.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The wait carries no value; the leader's payload is reference-counted and handing it across
        /// threads would mean racing its release. Re-probing instead reuses <c>TryGet</c>'s retain-and-
        /// recheck, and is automatically right when the leader's reply turned out not to be cacheable.
        /// </para>
        /// <para>
        /// The fallback send is not a failure path - it is what this caller would have done anyway without
        /// coalescing, so the worst case is exactly today's behaviour plus one wait.
        /// </para>
        /// <para>
        /// The wait is bounded by the leader's own request rather than by this caller's token: the leader
        /// always completes its fill, including when it throws. A caller with a shorter deadline than the
        /// leader therefore waits longer than it asked to, which is the one rough edge here.
        /// </para>
        /// </remarks>
#if NET6_0_OR_GREATER
        [AsyncMethodBuilder(typeof(System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder<>))]
#endif
        private static async ValueTask<TResult> AwaitShared<TResult>(
            RespExecutorBase executor,
            RespRequest owned,
            Task pending,
            IRespHandler<TResult> handler,
            RespClientCache cache,
            long maxAgeTicks,
            CancellationToken cancellationToken)
        {
            try
            {
                await pending.ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();

                if (cache.TryGet(owned, executor.Database, maxAgeTicks, out var hit))
                {
                    try
                    {
                        return Parse(handler, hit);
                    }
                    finally
                    {
                        hit.Release();
                    }
                }

                var response = await executor.SendAsync(owned, cancellationToken).ConfigureAwait(false);
                try
                {
                    return Parse(handler, response);
                }
                finally
                {
                    response?.Release();
                }
            }
            finally
            {
                owned.Dispose();
            }
        }

        /// <summary>The awaiting tail of an uncached send.</summary>
        /// <typeparam name="TResult">What the handler makes of the reply.</typeparam>
        /// <param name="executor">The executor to send through.</param>
        /// <param name="request">The rendered request; consumed by this call.</param>
        /// <param name="handler">Parses the reply.</param>
        /// <param name="cancellationToken">Cancels the send.</param>
        /// <returns>The parsed reply.</returns>
        /// <remarks>
        /// <para>
        /// <b>Pooled, and this is the single largest allocation on the path.</b> The synchronous part of
        /// <c>SendAsync</c> allocates nothing - a cache hit is measurably 0 bytes - so everything an
        /// awaited send costs above the executor is <i>this method's state machine</i>, boxed because the
        /// await genuinely suspends. Measured at ~176 bytes of the ~497 total; pooling the box took the
        /// whole path from 497 to 296 bytes per operation, which is what moved the new core from losing
        /// on allocation to winning.
        /// </para>
        /// <para>
        /// <b>What it costs, stated plainly:</b> the returned <see cref="ValueTask{TResult}"/> becomes
        /// single-consumption. Awaiting it twice throws rather than returning the same answer twice, where
        /// a <c>Task</c>-backed one would have tolerated it. That is already what
        /// <see cref="ValueTask{TResult}"/> documents - awaiting more than once has never been legal - so
        /// this enforces the existing contract rather than narrowing it. <c>AsTask()</c> still works, once.
        /// </para>
        /// <para>
        /// net6.0+ only, because that is where the pooling builder exists. Down-level TFMs get the
        /// ordinary builder and the ordinary allocation; nothing else differs.
        /// </para>
        /// </remarks>
#if NET6_0_OR_GREATER
        [AsyncMethodBuilder(typeof(System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder<>))]
#endif
        internal static async ValueTask<TResult> AwaitUncached<TResult>(
            RespExecutorBase executor,
            RespRequest request,
            IRespHandler<TResult> handler,
            CancellationToken cancellationToken)
        {
            try
            {
                var response = await executor.SendAsync(request, cancellationToken).ConfigureAwait(false);
                try
                {
                    return Parse(handler, response);
                }
                finally
                {
                    response?.Release();
                }
            }
            catch (RedisServerException ex) when (AuthFault(executor, ex) is { } authFault)
            {
                throw authFault;
            }
            finally
            {
                request.Dispose();
            }
        }
    }
}
