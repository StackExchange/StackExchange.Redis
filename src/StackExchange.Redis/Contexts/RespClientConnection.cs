using System;
using System.Collections.Generic;
using System.Net;
using RESPite.Messages;
using RESPite.Operations;

namespace StackExchange.Redis
{
    /// <summary>
    /// Decides what to do about a redirect. Supplied by whoever knows the cluster's shape.
    /// </summary>
    /// <param name="redirect">What the server said.</param>
    /// <param name="operation">The command that was redirected; not yet completed.</param>
    /// <returns>
    /// Whether the operation has been taken over. <see langword="false"/> leaves it to be completed with
    /// the reply as an ordinary error, which is the right answer when the redirect cannot be followed.
    /// </returns>
    internal delegate bool RespRedirectRouter(in RespRedirect redirect, RespPayloadOperation operation);

    /// <summary>Handles an out-of-band frame; says what should happen to it.</summary>
    /// <param name="frame">The complete frame, including its prefix.</param>
    internal delegate RespOutOfBandResult RespPushHandler(ReadOnlySpan<byte> frame);

    /// <summary>What inspecting an out-of-band frame concluded.</summary>
    /// <remarks>
    /// <b>Three outcomes, not two, and the third is not a nicety.</b> A subscribe or unsubscribe
    /// confirmation arrives as a push in RESP3 and <i>is</i> the reply to a command, so it must be matched
    /// rather than consumed - while a push nobody recognises must be dropped, because matching it to
    /// whichever command happens to be pending answers that command with somebody else's frame and
    /// desynchronises every reply after it. Collapsing those two into "not consumed" gets one of them
    /// silently wrong, and it is the expensive one.
    /// </remarks>
    internal enum RespOutOfBandResult
    {
        /// <summary>Not identified; for a push this means drop it, for an array it means treat it as a reply.</summary>
        NotRecognized,

        /// <summary>Consumed as a delivery; nothing further to do.</summary>
        Handled,

        /// <summary>Recognised, but it answers a command we sent, so ordinary matching must complete it.</summary>
        MatchToCommand,
    }

    /// <summary>
    /// EXPERIMENTAL SPIKE. The connection the client uses: one that knows what a reply <i>means</i>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Renamed from <c>RespRedirectingConnection</c>, because it grew a second job and the old name
    /// hid it.</b> Two kinds of reply do not complete the operation they are matched to - a redirect,
    /// which means "ask somewhere else", and a <c>+QUEUED</c> receipt inside <c>MULTI</c>, which means
    /// "your real answer comes with <c>EXEC</c>". Both are handled here, through the same hand-off hook,
    /// and neither needs anything from RESPite.
    /// </para>
    /// <para>
    /// The name mattered: a bare <c>RespConnection</c> looks interchangeable with this one and silently
    /// is not - a transaction over one completes every command with the string "QUEUED". That is exactly
    /// the mistake this rename exists to stop.
    /// </para>
    /// </remarks>
    /// <remarks>
    /// <para>
    /// <b>The following happens on the IO loop, and that is a correctness requirement rather than an
    /// optimisation.</b> Commands redirected together were sent to the wrong node in order, and that node
    /// answers each in that same order; re-issuing them as the replies arrive puts them on the new
    /// connection in the caller's original order. Handing the work to a thread pool would lose exactly
    /// that, and per-slot ordering is the strongest guarantee this client has to offer.
    /// </para>
    /// <para>
    /// The re-issue itself is not done here - this knows one connection and a redirect names a different
    /// node - so the decision is delegated upward to whoever holds the topology.
    /// </para>
    /// </remarks>
    internal sealed class RespClientConnection(
        RESPite.Transports.DuplexTransport transport,
        RespRedirectRouter router,
        bool includeDetailInExceptions = true,
        System.Buffers.MemoryPool<byte>? receiveBufferPool = null)
        : RespConnection(transport, receiveBufferPool), IRespPreambleTarget
    {
        private HashSet<long>? _claims;

        /// <summary>
        /// Whether deliveries on this connection can arrive as ordinary arrays, not only as push frames.
        /// </summary>
        /// <remarks>
        /// <b>True for a RESP2 subscription connection and nothing else.</b> RESP3 marks a delivery with
        /// its own prefix, so the protocol says what a frame is; RESP2 does not, and the only thing
        /// separating a <c>message</c> from a reply is that this connection is a subscriber. Set it on any
        /// other connection and ordinary replies start being eaten as deliveries.
        /// </remarks>
        internal bool DeliversArrays { get; set; }

        /// <summary>Told about each out-of-band frame; returns whether it was recognised and consumed.</summary>
        /// <remarks>
        /// Consumed means "never matched to a pending operation". Returning true for a frame that was
        /// actually a reply stalls whatever was waiting for it, so an unrecognised frame answers false and
        /// takes its chances as a reply - which is the right way round for RESP2, where the distinction is
        /// a guess in the first place.
        /// </remarks>
        internal RespPushHandler? OnPush { get; set; }

        /// <summary>The server this connection reaches; set once the endpoint is known.</summary>
        /// <remarks>
        /// <para>
        /// Borrowed from the client's model rather than reinvented: <see cref="ServerEndPoint"/> already
        /// holds the script-cache belief and flushes it when a server's identity changes underneath, which
        /// is exactly the behaviour a preamble gate wants and is not worth a second implementation of.
        /// </para>
        /// <para>
        /// <b>Held WEAKLY, which is a leak fix rather than a nicety, and the v3 core did the same thing
        /// for the same reason.</b> A connection runs a read loop for as long as it is open, so the
        /// loop's pending read keeps the connection reachable - and a <see cref="ServerEndPoint"/> holds
        /// its multiplexer, so a strong reference here let an open socket keep the whole multiplexer
        /// alive. A caller who abandons a multiplexer without disposing it then never gets it collected;
        /// <c>GarbageCollectionTests.MuxerIsCollected</c> is written for that caller. <c>PhysicalConnection</c>
        /// reaches its own bridge through a <see cref="WeakReference"/> and calls the property
        /// <c>BridgeCouldBeNull</c> to say so out loud.
        /// </para>
        /// <para>
        /// So this can go null while the connection is still open, and every caller already had to handle
        /// that: the server is unknown until the endpoint is resolved, so null was always possible. What
        /// it means afterwards is "nobody is modelling this server any more", and the honest response is
        /// the same as before it was known - carry on without the belief.
        /// </para>
        /// </remarks>
        internal ServerEndPoint? Server
        {
            get => _server is not null && _server.TryGetTarget(out var server) ? server : null;
            set => _server = value is null ? null : new WeakReference<ServerEndPoint>(value);
        }

        private WeakReference<ServerEndPoint>? _server;

        /// <inheritdoc/>
        ServerEndPoint? IRespPreambleTarget.Server => Server;

        /// <inheritdoc/>
        /// <remarks>
        /// Connection-local, and deliberately not thread-safe beyond the lock: claims are taken while the
        /// pair is being written, which happens under the connection's write lock, so contention here is
        /// the same contention that already serialises the write.
        /// </remarks>
        bool IRespPreambleTarget.TryClaim(long id)
        {
            lock (this)
            {
                return (_claims ??= new()).Add(id);
            }
        }

        /// <summary>Forget a connection-local fact, once the server has been told to forget it too.</summary>
        /// <param name="id">What was claimed.</param>
        /// <remarks>
        /// Without this a claim outlives what it describes: a discarded <c>HashImport</c> field-set stays
        /// "prepared here" for the life of the connection, so the set grows with every field-set a long-lived
        /// connection ever used - the v3 connection dropped it as its <c>DISCARD</c> was written.
        /// </remarks>
        internal void ReleaseClaim(long id)
        {
            lock (this)
            {
                _claims?.Remove(id);
            }
        }

        /// <inheritdoc/>
        /// <remarks>
        /// <para>
        /// A push frame always; an array only when this connection delivers them - and then only when it
        /// is not the reply to a <c>PING</c>, which on a subscriber connection is <i>also</i> an array.
        /// Without that exception the ping is consumed as a delivery and whoever sent it waits for ever,
        /// which is precisely the failure the v3 reader's <c>IsArrayPong</c> existed to avoid.
        /// </para>
        /// </remarks>
        protected override bool IsOutOfBand(ReadOnlySpan<byte> frame)
        {
            if (frame.IsEmpty) return false;

            var prefix = (RespPrefix)frame[0];
            if (prefix == RespPrefix.Push) return true;

            return prefix == RespPrefix.Array && DeliversArrays && !IsPong(frame);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// <para>
        /// Returning true consumes the frame; returning false lets it be matched as a reply. The mapping
        /// differs by prefix precisely where it matters:
        /// </para>
        /// <para>
        /// A PUSH that nobody recognises is <b>dropped</b>. A push is out-of-band by definition, so
        /// matching it would answer somebody's command with an unrelated frame and every reply after it
        /// would be off by one. An unrecognised ARRAY is matched instead, because this connection only
        /// GUESSED it was a delivery and dropping it would lose a real reply on the strength of a guess.
        /// </para>
        /// <para>
        /// Either prefix can also be a confirmation - subscribe and unsubscribe answer as pushes in RESP3 -
        /// which is why the handler can say "this is a reply" rather than only yes or no.
        /// </para>
        /// </remarks>
        protected override bool OnOutOfBand(ReadOnlySpan<byte> frame)
        {
            // GROUND TRUTH FIRST: a subscription push whose kind is the command at the head of the queue IS
            // that command's reply, whatever the dispatcher would have guessed. The server answers strictly
            // in wire order, and the queue is in wire order, so there is nothing to infer.
            //
            // The guess this replaces was wrong in a way that cascaded. The dispatcher decides whether a
            // `sunsubscribe` is unsolicited (a slot migrating away) or solicited (our own SUNSUBSCRIBE) from
            // `Subscription.HasSendInFlight` - which is only ever set for SUBSCRIBE sends. So the reply to our
            // own SUNSUBSCRIBE read as unsolicited and was consumed, and every reply after it then went to the
            // wrong command: the ssubscribe confirmation to the SUNSUBSCRIBE, a PING's PONG to the next
            // SSUBSCRIBE, and the PING itself was left waiting for an answer that had already been spent.
            // Traced frame by frame on `RetirementUnderMaintenanceTests`, where that stranded PING was the
            // tracer of a reconfiguration, which then held the reconfiguration lock for its whole relaxed
            // timeout - so the topology was never re-read and a dead node was never retired.
            //
            // The v3 core asked the same question the same way: `PhysicalConnection.Read` checked
            // `PeekChannelMessage(RedisCommand.SUNSUBSCRIBE, ...)` - its own outstanding commands - before
            // treating a `sunsubscribe` as unsolicited.
            if (SubscriptionCommandOf(frame) is { } answers
                && TryPeekPending(out var head)
                && head is RespPayloadOperation { Command: var waiting }
                && waiting == answers)
            {
                return false; // match it to the command that is waiting for it
            }

            var verdict = OnPush?.Invoke(frame) ?? RespOutOfBandResult.NotRecognized;
            return verdict switch
            {
                RespOutOfBandResult.Handled => true,
                RespOutOfBandResult.MatchToCommand => HeadIsKnownNotSubscription(),
                _ => (RespPrefix)frame[0] == RespPrefix.Push, // unrecognised: drop a push, match an array
            };
        }

        /// <summary>The command a subscription push is the reply to, if it is one.</summary>
        /// <param name="frame">The out-of-band frame.</param>
        /// <returns>The (un)subscribe command matching the push's kind, or null for anything else.</returns>
        /// <remarks>
        /// Reads only the kind, using the same parser the dispatcher uses, so the two cannot disagree about
        /// what a frame is. Works for a RESP3 push and a RESP2 array alike, since both lead with the kind.
        /// </remarks>
        private static unsafe RedisCommand? SubscriptionCommandOf(ReadOnlySpan<byte> frame)
        {
            var reader = new RespReader(frame);
            if (!(reader.SafeTryMoveNext() & reader.IsAggregate & !reader.IsStreaming)) return null;
            if (reader.AggregateLength() < 2) return null;
            if (!(reader.SafeTryMoveNext() & reader.IsInlineScalar & !reader.IsError)) return null;
            if (!reader.TryParseScalar(&PushKindMetadata.TryParse, out PushKind kind)) return null;

            return kind switch
            {
                PushKind.Subscribe => RedisCommand.SUBSCRIBE,
                PushKind.PSubscribe => RedisCommand.PSUBSCRIBE,
                PushKind.SSubscribe => RedisCommand.SSUBSCRIBE,
                PushKind.Unsubscribe => RedisCommand.UNSUBSCRIBE,
                PushKind.PUnsubscribe => RedisCommand.PUNSUBSCRIBE,
                PushKind.SUnsubscribe => RedisCommand.SUNSUBSCRIBE,
                _ => null,
            };
        }

        /// <summary>Whether a confirmation push would be matched to a command it cannot belong to.</summary>
        /// <remarks>
        /// <b>A confirmation push is only a reply if a subscription command is waiting for one.</b> The rule
        /// in this class's own remarks - matching a push "would answer somebody's command with an unrelated
        /// frame and every reply after it would be off by one" - was applied to UNRECOGNISED pushes, and a
        /// confirmation the dispatcher marked <c>MatchToCommand</c> went straight to whatever was at the head
        /// of the pending queue, whatever command that was.
        /// <para>
        /// On RESP3 that is a live hazard rather than a theoretical one, because one connection carries both
        /// the commands and the subscriptions. After a slot migration this client resubscribes, and a
        /// confirmation can arrive while a <c>SPUBLISH</c> is at the head: the publish was answered with an
        /// <c>ssubscribe</c> push, its real reply then had nothing to match, and the caller timed out
        /// holding a command the server had already executed - the message was delivered.
        /// <c>ClusterShardedTests.KeepSubscribedThroughSlotMigrationAsync</c> (RESP3) is that sequence.
        /// </para>
        /// <para>
        /// So a stray confirmation is dropped, which costs nothing: it confirms a state this client already
        /// tracks, and the resubscribe machinery is what owns getting that state right.
        /// </para>
        /// <para>
        /// <b>Conservative on purpose: it drops only when it KNOWS the head is something else.</b> The first
        /// version asked the opposite question - "is a subscribe waiting?" - and that dropped the handshake's
        /// own configuration-channel <c>SUBSCRIBE</c> confirmation, because not every operation carries its
        /// command (<see cref="RedisCommand.NONE"/>), so every connect then waited out its full timeout. A
        /// head whose command is unknown, or that is not a command of this client's at all, is matched as
        /// before; only a head that is positively some OTHER command refuses the frame.
        /// </para>
        /// </remarks>
        /// <summary>The oldest command still waiting for its reply, for diagnostics; null when none is.</summary>
        internal IRespMessage? PendingHead => TryPeekPending(out var head) ? head : null;

        private bool HeadIsKnownNotSubscription()
            => TryPeekPending(out var head)
                && head is RespPayloadOperation { Command: not RedisCommand.NONE } operation
                && operation.Command is not (RedisCommand.SUBSCRIBE or RedisCommand.PSUBSCRIBE or RedisCommand.SSUBSCRIBE
                    or RedisCommand.UNSUBSCRIBE or RedisCommand.PUNSUBSCRIBE or RedisCommand.SUNSUBSCRIBE);

        /// <summary>Whether this array is the reply to a <c>PING</c> rather than a delivery.</summary>
        private static bool IsPong(ReadOnlySpan<byte> frame)
        {
            var reader = new RespReader(frame);
            if (!reader.SafeTryMoveNext() || reader.Prefix != RespPrefix.Array) return false;
            if (!reader.SafeTryMoveNext()) return false;

            Span<byte> buffer = stackalloc byte[4];
            var span = reader.TryGetSpan(out var direct) ? direct : reader.Buffer(buffer);
            return span.Length == 4
                && (span[0] | 0x20) == (byte)'p'
                && (span[1] | 0x20) == (byte)'o'
                && (span[2] | 0x20) == (byte)'n'
                && (span[3] | 0x20) == (byte)'g';
        }

        /// <inheritdoc/>
        /// <remarks>
        /// No lock of its own, and that is not an oversight: this is only ever called from inside the
        /// connection's write lock, which is the lock that matters - it is what makes the claim and the
        /// write that honours it one indivisible step. Taking a second lock here would order nothing extra
        /// and would invite the two to be acquired in different orders elsewhere.
        /// </remarks>
        bool IRespPreambleTarget.TrySelectDatabase(int database)
        {
            if (CurrentDatabase == database) return false;

            // recorded BEFORE the SELECT is written, because the caller writes it immediately and under
            // this same lock; a later reader is therefore either this writer or somebody who will ask again
            CurrentDatabase = database;
            return true;
        }

        /// <summary>Which database this connection is currently <c>SELECT</c>ed onto.</summary>
        /// <remarks>
        /// <para>
        /// <b>Connection state, and the reason a database index cannot simply be carried on the request.</b>
        /// <c>SELECT</c> is sticky: it changes the connection until something changes it back, so a command
        /// for another database has to be preceded by its own <c>SELECT</c>, and the two must reach the
        /// socket with nothing between them or the command runs against whatever the interloper selected.
        /// </para>
        /// <para>
        /// Set by the handshake and then only by the pair-write that changes it, both of which happen under
        /// the connection's write lock - so a reader of this is either the writer itself or somebody who
        /// will re-check under that lock before acting.
        /// </para>
        /// </remarks>
        /// <summary>What the server calls this connection, when it was asked.</summary>
        /// <remarks>
        /// <b>The client's own identity on the wire</b>, and the only way to point at this connection in
        /// <c>CLIENT LIST</c> or kill it by id. Reported through <c>IInternalConnectionMultiplexer.GetConnectionId</c>,
        /// which used to answer only about a bridge - so while both cores existed it answered null about
        /// the connection actually carrying the commands (<c>ConfigTests.GetClients</c>).
        /// </remarks>
        internal long? ConnectionId { get; set; }

        /// <summary>The address this connection actually reached, when it was an IP one.</summary>
        /// <remarks>
        /// Needed by a <c>MOVING</c> handoff, which polls for the endpoint to change away from the address
        /// it is on - so the address REACHED is the question, not the endpoint dialled. Reported by the
        /// transport factory, because only whoever built the socket knows it.
        /// </remarks>
        internal System.Net.IPAddress? RemoteAddress { get; set; }

        internal int CurrentDatabase { get; set; }

        /// <inheritdoc/>
        protected override bool TryHandOff(ReadOnlySpan<byte> frame, IRespMessage message)
        {
            // every frame matched to an operation passes through here, which makes it the natural place
            // to mark "answered" - and means RESPite needs no hook for it
            if (message is RespPayloadOperation answered)
            {
                answered.Profile?.SetResponseReceived();

                // a queued command's first reply is a +QUEUED receipt, not its result. Taking it here
                // leaves the operation pending, to be completed later from EXEC's array - which is what
                // the hand-off hook is for, and why this needs nothing from RESPite.
                if (answered.ExpectsQueuedReceipt)
                {
                    answered.ExpectsQueuedReceipt = false;
                    return true;
                }
            }

            // one byte of work for every reply that is not an error, which is nearly all of them
            if (!RespRedirect.TryParse(frame, out var redirect)) return false;
            if (message is not RespPayloadOperation operation) return false;

            // A redirect we cannot follow is not a redirect: "?" means the server does not know where the
            // slot went either, so there is nowhere to send this. Asked FIRST, ahead of the caller's
            // preferences, because it is a statement about the reply rather than about what to do with it
            // - a caller who said NoRedirect still wants to know the server could not name a target, and
            // the topology refresh is worth requesting either way. That ordering matches the v3 core,
            // which classified this before it consulted NoRedirect at all.
            if (redirect.IsUnroutable)
            {
                router(in redirect, operation);
                operation.UnroutableRedirectMessage = DescribeUnroutable(in redirect);
                return false;
            }

            // A caller can decline redirects outright, and NoRedirect is not a hint: the server's error is
            // surfaced unchanged. What uses it depends on that - a transaction's queued commands and the
            // topology probes must stay on the connection they chose, and a caller diagnosing a cluster
            // wants to be told where the server said the slot went rather than quietly following it.
            if ((operation.Flags & CommandFlags.NoRedirect) != 0)
            {
                // ...but SAY so, rather than restating the redirect the caller can already read. The raw
                // error is the one thing they have; what they are missing is that the client knew where to
                // send it and did not because they asked it not to.
                if (redirect.IsMoved) operation.DeclinedRedirectMessage = DescribeDeclined(in redirect, operation);
                return false;
            }

            // ONCE IS ENOUGH. The v3 core set NoRedirect when it re-issued, on the reasoning that a
            // second redirect for the same command is pathological rather than routine - a redirect loop
            // between two nodes that disagree, or a topology changing faster than commands complete. The
            // command fails with the server's own error, which says more than a hang would.
            if (operation.HasFollowedRedirect) return false;

            operation.HasFollowedRedirect = true;
            return router(in redirect, operation);
        }

        /// <summary>The error a caller sees when they declined a redirect the client could have followed.</summary>
        /// <param name="redirect">The redirect, whose target and slot are what the caller wants named.</param>
        /// <param name="operation">The command, so the message can name it.</param>
        /// <remarks>Worded as the v3 core worded it, because the people reading it are the same.</remarks>
        private string DescribeDeclined(in RespRedirect redirect, RespPayloadOperation operation)
            => includeDetailInExceptions
                ? $"Key has MOVED to Endpoint {redirect.Target} and hashslot {redirect.Slot} but CommandFlags.NoRedirect was specified - redirect not followed for {operation.CommandAndKey}. "
                : "Key has MOVED but CommandFlags.NoRedirect was specified - redirect not followed. ";

        /// <summary>The error a caller sees when the server named a target that cannot be dialled.</summary>
        /// <param name="redirect">The redirect, whose target is the whole of the information.</param>
        /// <remarks>
        /// Quotes what the server wrote, because with no routable endpoint there is nothing else to name -
        /// and the same text under <c>IncludeDetailInExceptions=false</c> is withheld for the same reason
        /// every other detail is, matching the v3 wording so the two read alike.
        /// </remarks>
        private string DescribeUnroutable(in RespRedirect redirect)
            => includeDetailInExceptions
                ? $"The server redirected hashslot {redirect.Slot} to '{redirect.Target}', which does not identify a node that can be connected to; a topology refresh has been requested. "
                : "The server redirected to an endpoint that does not identify a node that can be connected to; a topology refresh has been requested. ";
    }
}
