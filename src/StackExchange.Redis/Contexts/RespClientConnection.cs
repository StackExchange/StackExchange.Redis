using System;
using System.Net;
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
        RespRedirectRouter router) : RespConnection(transport)
    {
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
