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
    /// EXPERIMENTAL SPIKE. A connection that follows <c>-MOVED</c> and <c>-ASK</c> rather than
    /// reporting them.
    /// </summary>
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
    internal sealed class RespRedirectingConnection(
        RESPite.Transports.DuplexTransport transport,
        RespRedirectRouter router) : RespConnection(transport)
    {
        /// <inheritdoc/>
        protected override bool TryHandOff(ReadOnlySpan<byte> frame, IRespMessage message)
        {
            // every frame matched to an operation passes through here, which makes it the natural place
            // to mark "answered" - and means RESPite needs no hook for it
            if (message is RespPayloadOperation answered) answered.Profile?.SetResponseReceived();

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
