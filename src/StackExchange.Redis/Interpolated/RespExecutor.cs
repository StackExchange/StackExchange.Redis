using System;
using System.Diagnostics.CodeAnalysis;
using RESPite;

namespace StackExchange.Redis.Interpolated
{
    /// <summary>
    /// EXPERIMENTAL SPIKE. Something that can issue a rendered request.
    /// </summary>
    /// <remarks>
    /// One member to implement. The orchestration - cache probe, generation capture, payload lifetime - is
    /// in <see cref="RespExecutor"/> and is shared by every implementation rather than reimplemented by
    /// each, which is the point: the ordering rule that makes caching safe lives in one place we own.
    /// </remarks>
    [Experimental(Experiments.InterpolatedWriter, UrlFormat = Experiments.UrlFormat)]
    public interface IRespExecutor
    {
        /// <summary>The database requests run against; part of a cached entry's identity.</summary>
        int Database { get; }

        /// <summary>Issue the rendered request and return the raw reply.</summary>
        /// <param name="request">The rendered request frame.</param>
        /// <remarks>
        /// Returning <c>byte[]</c> is a spike convenience; the real thing would hand back the reply frame's
        /// own lease, as <c>RespResult</c> already does, rather than copying.
        /// </remarks>
        byte[] Send(ReadOnlySpan<byte> request);
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
        /// Do not let <paramref name="response"/> escape. The bytes belong to a pooled buffer that may be
        /// released as soon as this returns, and may then be serving another request entirely.
        /// </remarks>
        TResult Parse(ReadOnlySpan<byte> response);
    }

    /// <summary>
    /// EXPERIMENTAL SPIKE. Sending a request, with or without a client-side cache.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The cache is an <b>optional participant in the send</b>, not the entry point. That keeps the call
    /// site identical whether or not caching is configured - <c>executor.Send(request, handler)</c> versus
    /// <c>executor.Send(request, handler, cache)</c> - so enabling caching does not mean rewriting callers,
    /// and "no cache" is an ordinary case rather than a missing one.
    /// </para>
    /// <para>
    /// It is also the right layering. A cache that called the executor would have to sit above dispatch and
    /// know how to send; a cache the executor consults is what it actually is - a client-side concern of
    /// the thing doing the sending.
    /// </para>
    /// </remarks>
    [Experimental(Experiments.InterpolatedWriter, UrlFormat = Experiments.UrlFormat)]
    public static class RespExecutor
    {
        /// <summary>Send a request and parse the reply, with no caching.</summary>
        /// <typeparam name="TResult">What parsing the reply produces.</typeparam>
        /// <param name="executor">The executor to send through.</param>
        /// <param name="request">The rendered request; consumed by this call.</param>
        /// <param name="handler">Turns the reply into a result.</param>
        public static TResult Send<TResult>(
            this IRespExecutor executor,
            ref RespFrame request,
            IRespHandler<TResult> handler)
        {
            if (executor is null) throw new ArgumentNullException(nameof(executor));
            if (handler is null) throw new ArgumentNullException(nameof(handler));

            var response = executor.Send(request.Span);
            request.Dispose();
            return handler.Parse(response);
        }

        /// <summary>
        /// Send a request and parse the reply, serving it from <paramref name="cache"/> when possible and
        /// populating the cache when not.
        /// </summary>
        /// <typeparam name="TResult">What parsing the reply produces.</typeparam>
        /// <param name="executor">The executor to send through.</param>
        /// <param name="request">The rendered request; consumed by this call on every path.</param>
        /// <param name="handler">Turns the reply into a result.</param>
        /// <param name="cache">The cache to consult, or <c>null</c> to bypass caching entirely.</param>
        /// <remarks>
        /// <para>
        /// Three lifetimes are handled here so that no caller has to, in descending order of how easy each
        /// is to get wrong: the key generations are captured <b>before</b> the send, so an invalidation
        /// arriving while the command is in flight is detected rather than lost; the payload is retained
        /// across <see cref="IRespHandler{TResult}.Parse"/> and released in a <c>finally</c>; and the
        /// request frame is consumed on every path, whether or not it became a cache key.
        /// </para>
        /// <para>
        /// The first of those is the one that cannot be fixed after the fact. Look up, miss, send, then add
        /// has nothing left to compare against by the time it adds, and the server does not repeat an
        /// invalidation - so the entry would be stale permanently, not briefly.
        /// </para>
        /// <para>
        /// A reply that arrives after an invalidation is still parsed and returned: it is a legitimate answer
        /// for a read that raced a write, and the caller would have got it anyway without a cache. It is
        /// simply not stored.
        /// </para>
        /// </remarks>
        public static TResult Send<TResult>(
            this IRespExecutor executor,
            ref RespFrame request,
            IRespHandler<TResult> handler,
            RespClientCache? cache)
        {
            if (executor is null) throw new ArgumentNullException(nameof(executor));
            if (handler is null) throw new ArgumentNullException(nameof(handler));
            if (cache is null) return Send(executor, ref request, handler);

            var database = executor.Database;

            if (cache.TryGet(request.AsLookupKey(), database, out var hit))
            {
                request.Dispose();
                try
                {
                    return handler.Parse(hit.Span);
                }
                finally
                {
                    hit.Release();
                }
            }

            if (!cache.TryBeginFill(ref request, database, out var fill))
            {
                // keys not nameable, so not invalidatable, so not cacheable - but still answerable
                var uncacheable = executor.Send(request.Span);
                request.Dispose();
                return handler.Parse(uncacheable);
            }

            // generations were captured above, BEFORE this line; the frame's buffer belongs to the fill now
            var response = executor.Send(fill.Key.Span);
            if (!cache.TryComplete(fill, response, out var stored)) return handler.Parse(response);

            try
            {
                return handler.Parse(stored.Span);
            }
            finally
            {
                stored.Release();
            }
        }
    }
}
