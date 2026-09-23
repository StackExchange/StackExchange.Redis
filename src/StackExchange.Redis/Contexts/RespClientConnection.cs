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

    /// <summary>Handles an out-of-band frame; returns whether it was recognised and consumed.</summary>
    /// <param name="frame">The complete frame, including its prefix.</param>
    internal delegate bool RespPushHandler(ReadOnlySpan<byte> frame);

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
        RespRedirectRouter router) : RespConnection(transport), IRespPreambleTarget
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
        /// Borrowed from the old core rather than reinvented: <see cref="ServerEndPoint"/> already holds
        /// the script-cache belief and flushes it when a server's identity changes underneath, which is
        /// exactly the behaviour a preamble gate wants and is not worth a second implementation of while
        /// both cores exist.
        /// </remarks>
        internal ServerEndPoint? Server { get; set; }

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

        /// <inheritdoc/>
        /// <remarks>
        /// <para>
        /// A push frame always; an array only when this connection delivers them - and then only when it
        /// is not the reply to a <c>PING</c>, which on a subscriber connection is <i>also</i> an array.
        /// Without that exception the ping is consumed as a delivery and whoever sent it waits for ever,
        /// which is precisely the failure the shipped reader's <c>IsArrayPong</c> exists to avoid.
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
        /// <b>False when there is no handler, or it does not recognise the frame.</b> That is deliberate
        /// for arrays: this connection guessed that an array was a delivery, and if nobody claims it the
        /// honest fallback is to let it be matched as a reply rather than drop it. A RESP3 push that goes
        /// unrecognised is dropped by the base instead, which is right there - a push is out-of-band by
        /// definition and matching it to a command would desynchronise the stream.
        /// </remarks>
        protected override bool OnOutOfBand(ReadOnlySpan<byte> frame) => OnPush?.Invoke(frame) == true;

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

            // ONCE IS ENOUGH. The shipped core sets NoRedirect when it re-issues, on the reasoning that a
            // second redirect for the same command is pathological rather than routine - a redirect loop
            // between two nodes that disagree, or a topology changing faster than commands complete. The
            // command fails with the server's own error, which says more than a hang would.
            if (operation.HasFollowedRedirect) return false;

            // and a redirect we cannot follow is not a redirect: "?" means the server does not know
            // where the slot went either, so there is nowhere to send this. It becomes the error it
            // already is, and the topology refresh is the router's business.
            if (redirect.IsUnroutable)
            {
                router(in redirect, operation);
                return false;
            }

            operation.HasFollowedRedirect = true;
            return router(in redirect, operation);
        }
    }
}
