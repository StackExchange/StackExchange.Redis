using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using RESPite;

namespace StackExchange.Redis.Interpolated
{
    /// <summary>
    /// EXPERIMENTAL SPIKE. Something that can issue a rendered request.
    /// </summary>
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
    [Experimental(Experiments.InterpolatedWriter, UrlFormat = Experiments.UrlFormat)]
    public interface IRespExecutor
    {
        /// <summary>The database requests run against; part of a cached entry's identity.</summary>
        int Database { get; }

        /// <summary>Issue the request and return the reply, with one reference held by the caller.</summary>
        /// <param name="request">The rendered request; retain it if it must outlive this call.</param>
        RespPayload Send(in RespRequest request);

        /// <summary>Issue the request asynchronously.</summary>
        /// <param name="request">The rendered request; retain it if it must outlive this call.</param>
        /// <param name="cancellationToken">Cancels the send.</param>
        ValueTask<RespPayload> SendAsync(RespRequest request, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// EXPERIMENTAL SPIKE. Turns a reply into a result - the <c>ResultProcessor</c> half.
    /// </summary>
    /// <typeparam name="TResult">What parsing the reply produces.</typeparam>
    [Experimental(Experiments.InterpolatedWriter, UrlFormat = Experiments.UrlFormat)]
    public interface IRespHandler<out TResult>
    {
        /// <summary>Read a reply - cached or fresh - into a result.</summary>
        /// <param name="response">The reply bytes; valid only for the duration of this call.</param>
        /// <remarks>
        /// A span is right here, unlike on <see cref="IRespExecutor"/>: parsing is synchronous and happens
        /// inside the window where the payload is retained. Do not let it escape - the bytes belong to a
        /// pooled buffer that may be released as soon as this returns.
        /// </remarks>
        TResult Parse(ReadOnlySpan<byte> response);
    }

    /// <summary>
    /// EXPERIMENTAL SPIKE. Sending a request, with or without a client-side cache.
    /// </summary>
    /// <remarks>
    /// The cache is an <b>optional participant in the send</b>, not the entry point. The call site is
    /// identical whether or not caching is configured, so enabling it does not mean rewriting callers, and
    /// "no cache" is an ordinary case rather than a missing one. It is also the right layering: a cache that
    /// called the executor would have to sit above dispatch and know how to send.
    /// </remarks>
    [Experimental(Experiments.InterpolatedWriter, UrlFormat = Experiments.UrlFormat)]
    public static class RespExecutor
    {
        /// <summary>
        /// Send a request and parse the reply, optionally serving it from - and populating - the context's cache.
        /// </summary>
        /// <typeparam name="TResult">What parsing the reply produces.</typeparam>
        /// <param name="context">The context to send through; supplies the executor, cache and cancellation.</param>
        /// <param name="request">The rendered request; consumed by this call on every path.</param>
        /// <param name="handler">Turns the reply into a result.</param>
        /// <param name="flags">
        /// The command's flags. Caching additionally requires a declared retry category no more severe than
        /// <see cref="CommandFlags.CommandRetryReadOnly"/>; see
        /// <see cref="RespClientCache.TryBeginFill(ref RespFrame, int, CommandFlags, out RespClientCache.RespFill)"/>.
        /// </param>
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
        public static TResult Send<TResult>(
            this in RespContext context,
            ref RespFrame request,
            IRespHandler<TResult> handler,
            CommandFlags flags)
        {
            if (handler is null) throw new ArgumentNullException(nameof(handler));
            var executor = context.Executor ?? ThrowNoExecutor(ref request);
            var cache = context.Cache;

            // NoClientCache suppresses the PROBE as well as the store: opting out must mean the caller does
            // not get a cached answer either, not merely that this reply is not kept
            if (cache is not null && cache.PermitsCaching(flags))
            {
                if (TryServeFromCache(executor, ref request, handler, cache, out var cached)) return cached;

                if (cache.TryBeginFill(ref request, executor.Database, flags, out var fill))
                {
                    // generations captured above, BEFORE this send
                    var filled = executor.Send(fill.Key);
                    try
                    {
                        cache.TryComplete(fill, filled);
                        return handler.Parse(filled.Span);
                    }
                    finally
                    {
                        filled.Release();
                    }
                }

                // not cacheable, which is precisely the uncached case - fall through to it
            }

            // the executor may need the bytes past this call, so hand it something it can retain
            var owned = request.Detach();
            try
            {
                var response = executor.Send(owned);
                try
                {
                    return handler.Parse(response.Span);
                }
                finally
                {
                    response.Release();
                }
            }
            finally
            {
                owned.Dispose();
            }
        }

        /// <inheritdoc cref="Send{TResult}(in RespContext, ref RespFrame, IRespHandler{TResult}, CommandFlags)"/>
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
        public static ValueTask<TResult> SendAsync<TResult>(
            this in RespContext context,
            ref RespFrame request,
            IRespHandler<TResult> handler,
            CommandFlags flags)
        {
            if (handler is null) throw new ArgumentNullException(nameof(handler));
            var executor = context.Executor ?? ThrowNoExecutor(ref request);
            var cache = context.Cache;
            var cancellationToken = context.CancellationToken;

            if (cache is not null && cache.PermitsCaching(flags))
            {
                if (TryServeFromCache(executor, ref request, handler, cache, out var cached))
                {
                    return new ValueTask<TResult>(cached);
                }

                if (cache.TryBeginFill(ref request, executor.Database, flags, out var fill))
                {
                    return AwaitFill(executor, fill, handler, cache, cancellationToken);
                }
            }

            return AwaitUncached(executor, request.Detach(), handler, cancellationToken);
        }

        [DoesNotReturn]
        private static IRespExecutor ThrowNoExecutor(ref RespFrame request)
        {
            request.Dispose();
            throw new InvalidOperationException("No executor is configured on this context.");
        }

        // the cache probe is identical for both, and borrows rather than detaching: on a HIT the request
        // never reaches the executor, so it never needs an owned lease
        private static bool TryServeFromCache<TResult>(
            IRespExecutor executor,
            ref RespFrame request,
            IRespHandler<TResult> handler,
            RespClientCache cache,
            [MaybeNullWhen(false)] out TResult result)
        {
            if (!cache.TryGet(request.AsLookupKey(), executor.Database, out var hit))
            {
                result = default;
                return false;
            }

            request.Dispose();
            try
            {
                result = handler.Parse(hit.Span);
                return true;
            }
            finally
            {
                hit.Release();
            }
        }

        private static async ValueTask<TResult> AwaitFill<TResult>(
            IRespExecutor executor,
            RespClientCache.RespFill fill,
            IRespHandler<TResult> handler,
            RespClientCache cache,
            CancellationToken cancellationToken)
        {
            var response = await executor.SendAsync(fill.Key, cancellationToken).ConfigureAwait(false);
            try
            {
                cache.TryComplete(fill, response);
                return handler.Parse(response.Span);
            }
            finally
            {
                response.Release();
            }
        }

        private static async ValueTask<TResult> AwaitUncached<TResult>(
            IRespExecutor executor,
            RespRequest request,
            IRespHandler<TResult> handler,
            CancellationToken cancellationToken)
        {
            try
            {
                var response = await executor.SendAsync(request, cancellationToken).ConfigureAwait(false);
                try
                {
                    return handler.Parse(response.Span);
                }
                finally
                {
                    response.Release();
                }
            }
            finally
            {
                request.Dispose();
            }
        }
    }
}
