using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using RESPite;
using RESPite.Messages;
using StackExchange.Redis.Caching;
using StackExchange.Redis.Protocol;

namespace StackExchange.Redis
{
    /// <summary>
    /// EXPERIMENTAL SPIKE. Something that can issue a rendered request.
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

        /// <summary>Issue the request and return the reply, with one reference held by the caller.</summary>
        /// <param name="request">The rendered request; retain it if it must outlive this call.</param>
        public abstract RespPayload Send(in RespRequest request);

        /// <summary>Issue the request asynchronously.</summary>
        /// <param name="request">The rendered request; retain it if it must outlive this call.</param>
        /// <param name="cancellationToken">Cancels the send.</param>
        public abstract ValueTask<RespPayload> SendAsync(RespRequest request, CancellationToken cancellationToken = default);

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
        public virtual bool IsConnected(in RedisKey key, CommandFlags flags) => true;

        /// <summary>Whether <see cref="SendAsync(RespRequest, RespRequest, IRespPreambleGate?, CancellationToken)"/> does anything useful.</summary>
        /// <remarks>
        /// Asked rather than type-tested. The default answer is no, and the default implementation throws,
        /// so an executor that has not thought about preambles is not quietly assumed to support them.
        /// </remarks>
        public virtual bool CanWritePreamble => false;

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
    /// An executor that can write several requests as <b>one run</b>: consecutive, on one connection, with
    /// nothing of anybody else's between them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>PROVISIONAL, and still an interface for that reason.</b> The shape is under review - the spans
    /// forbid an <c>async</c> implementation, so <c>RespRetryExecutor</c> cannot offer it and a retrying
    /// batch silently degrades to a pipeline; see the queue. It stays separate rather than becoming a
    /// virtual on <see cref="RespExecutorBase"/> until the shape settles, because a member on the base is
    /// a promise to every executor and this one is not ready to make it.
    /// </para>
    /// <para>
    /// <b>The run must resolve to a single slot.</b> One connection means one server, so every request that
    /// names a key has to agree about which one - the slots are combined, and a run that spans two is
    /// refused by routing rather than silently split. That is the constraint the shape cannot express and
    /// this paragraph has to: splitting a run across servers is the caller's problem, and is why the
    /// shipped <c>RedisBatch.Execute</c> groups per bridge before it writes anything.
    /// </para>
    /// <para>
    /// <b>Contiguity is a WRITE-side guarantee, and only that.</b> It is tempting to read "one run" as "N
    /// replies in a row", and that is emphatically not what comes back. Between any two of this run's
    /// replies the stream may carry a RESP3 out-of-band push (a pub/sub message, a client-side-caching
    /// invalidation), the reply to a <c>SELECT</c> the write path injected because the connection was on
    /// another database, a high-integrity response token, or a preamble's reply such as
    /// <c>SCRIPT LOAD</c>'s. The n'th frame is not the n'th reply, and nothing may assume it is.
    /// </para>
    /// <para>
    /// All of that is demultiplexed below, which is precisely why this hands back a <b>task per request</b>
    /// rather than one result holding N payloads: the replies are N independent events that happen to have
    /// been asked for together. Only the <i>writing</i> was ever a single thing. A caller that wants them
    /// gathered can gather them - but that is a convenience built on the tasks, not a shape the wire
    /// supports.
    /// </para>
    /// </remarks>
    internal interface IRespRunExecutor
    {
        /// <summary>Write every request as one run, and hand back a reply task for each.</summary>
        /// <param name="run">The requests, in the order they should reach the server.</param>
        /// <param name="replies">Filled with one task per request, in the same order; same length as <paramref name="run"/>.</param>
        /// <param name="cancellationToken">Cancels the send.</param>
        /// <returns>Completes when the final request has been answered.</returns>
        /// <remarks>
        /// <b>Two levels, because a run has two meanings.</b> Each request has its own reply and its own
        /// caller waiting for it - that is <paramref name="replies"/>, and it is the honest one. The return
        /// value is the convenience: replies to pipelined commands come back in order on a connection, so
        /// the last request being answered is ordinarily the run being done. <b>Ordinarily</b>, because a
        /// redirection can re-dispatch one command elsewhere and land its reply after the others - so a
        /// caller that needs "all of them" should await <paramref name="replies"/>, which is what
        /// <c>RespBatchExecutor</c> does.
        /// </remarks>
        ValueTask SendAsync(
            scoped ReadOnlySpan<RespRequest> run,
            scoped Span<ValueTask<RespPayload>> replies,
            CancellationToken cancellationToken = default);
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
    /// EXPERIMENTAL SPIKE. Sending a request, with or without a client-side cache.
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
                else
                {
                    // sequential fallback: wait for the preamble, then send. Ordering is what matters, and
                    // awaiting gives it - at the cost of the round trip a unit would have saved. The gate is
                    // not consulted here: it asks about a connection, and this path has no notion of one.
                    (await executor.SendAsync(head, cancellationToken).ForAwait())?.Release();
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
            var category = flags & Message.MaskRetryCategory;
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
        private static void DemandNoCancellation(ref RespRequestBuilder request, CancellationToken cancellationToken)
        {
            // already cancelled is the half we CAN honour - refusing to start costs nothing - so it gets the
            // right exception rather than "not implemented". Checked first, because a cancelled token is
            // also a cancellable one.
            if (cancellationToken.IsCancellationRequested)
            {
                request.Dispose();
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (cancellationToken.CanBeCanceled)
            {
                request.Dispose();
                DemandNoCancellation(cancellationToken);
            }
        }

        private static void DemandNoCancellation(CancellationToken cancellationToken)
        {
            // as above: cancelled-before-we-started is honoured properly, because it can be
            cancellationToken.ThrowIfCancellationRequested();

            if (cancellationToken.CanBeCanceled)
            {
                throw new NotImplementedException(
                    "Cancellation is not yet supported on this surface: the underlying pipeline cannot cancel an "
                    + "in-flight request, so honouring the token is not possible yet. Pass 'default' until it is.");
            }
        }

        private static TResult Parse<TResult>(IRespHandler<TResult> handler, RespPayload? response)
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
            DemandNoCancellation(cancellationToken);

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
                // deprioritised (see TransitionalDatabase), so this is a deliberate gap rather than an
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
            DemandNoCancellation(cancellationToken);

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

            return AwaitUncached(executor, request.Detach(flags), handler, cancellationToken);
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
            DemandNoCancellation(ref request, cancellationToken);
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
        /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
        public static ValueTask SendAsync(
            this RespContext context,
            [InterpolatedStringHandlerArgument(nameof(context))] ref RespRequestBuilder request,
            CommandFlags flags = CommandFlags.None,
            CancellationToken cancellationToken = default)
        {
            DemandNoCancellation(ref request, cancellationToken);
            var frame = request.Complete();
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
        /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
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
        /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
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
        /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
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
            DemandNoCancellation(ref request, cancellationToken);
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
        private static async ValueTask<TResult> AwaitUncached<TResult>(
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
            finally
            {
                request.Dispose();
            }
        }
    }
}
