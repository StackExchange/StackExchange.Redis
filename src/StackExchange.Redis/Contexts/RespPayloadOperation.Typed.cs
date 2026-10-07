using System;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;
using StackExchange.Redis.Protocol;

namespace StackExchange.Redis
{
    /// <summary>
    /// An operation that is also the awaitable for its <b>parsed</b> result.
    /// </summary>
    /// <typeparam name="TResult">What the handler makes of the reply.</typeparam>
    /// <remarks>
    /// <para>
    /// <b>One object per send instead of two.</b> The untyped operation completes with a raw payload, and the
    /// caller's typed result came from an async method that awaited it and then parsed - whose state machine is
    /// boxed whenever the reply is not already there. The pooling builder hands those boxes back only while
    /// few are alive, and a deep pipeline keeps hundreds of thousands in flight: every send then allocated an
    /// operation AND a box, and the box was a third of all a send allocated. Here the operation carries the
    /// handler and parses in <see cref="IValueTaskSource{TResult}.GetResult"/>, so there is no box to make.
    /// </para>
    /// <para>
    /// <b>Its own pool per result type</b>, because an instance can only ever be handed out as the type it
    /// is. The untyped interface still works on it - the endpoint executor treats every operation as a
    /// <see cref="RespPayloadOperation"/> - which is why this derives rather than duplicates.
    /// </para>
    /// <para>
    /// Not used for a <see cref="RespPayload"/> result: the two <see cref="IValueTaskSource{TResult}"/> would
    /// be one interface, and the raw payload has no parse to absorb anyway.
    /// </para>
    /// </remarks>
    internal sealed class RespPayloadOperation<TResult> : RespPayloadOperation, IValueTaskSource<TResult>, IValueTaskSource
    {
        private static readonly RespPayloadOperation<TResult>?[] TypedPool = new RespPayloadOperation<TResult>?[PoolSize];

        private IRespHandler<TResult>? _handler;
        private RespExecutorBase? _executor;

        [ThreadStatic]
        private static RespPayloadOperation<TResult>? t_dispatched;

        /// <summary>Note this as the operation this thread has just handed back as a task.</summary>
        /// <remarks>See <see cref="TryTakeDispatched(ValueTask{TResult}, out short)"/>.</remarks>
        internal void NoteDispatched() => t_dispatched = this;

        /// <summary>
        /// Recover the operation behind a task this thread was just given, without reaching into <see cref="ValueTask{TResult}"/>.
        /// </summary>
        /// <param name="pending">The task to identify.</param>
        /// <param name="token">The operation's token, when it is ours.</param>
        /// <returns>The operation, or null if <paramref name="pending"/> is anything else.</returns>
        /// <remarks>
        /// <para>
        /// For the <c>IDatabase</c> bridge, which turns this task into a <see cref="Task"/>: knowing the source, it can
        /// register on it with a STATIC callback, where going through the task's awaiter costs a delegate per command.
        /// </para>
        /// <para>
        /// <b>Safe because it is checked, not trusted.</b> <see cref="ValueTask{TResult}.Equals(ValueTask{TResult})"/>
        /// compares the source and the token, so a stale note - a cache hit in between, a decorator's own task, another
        /// command - simply fails to match, and the caller falls back to the awaiter.
        /// </para>
        /// </remarks>
        internal static RespPayloadOperation<TResult>? TryTakeDispatched(ValueTask<TResult> pending, out short token)
        {
            var noted = t_dispatched;
            t_dispatched = null;
            if (noted is not null)
            {
                token = noted.Token;
                if (pending.Equals(new ValueTask<TResult>(noted, token))) return noted;
            }

            token = 0;
            return null;
        }

        /// <inheritdoc cref="TryTakeDispatched(ValueTask{TResult}, out short)"/>
        internal static RespPayloadOperation<TResult>? TryTakeDispatched(ValueTask pending, out short token)
        {
            var noted = t_dispatched;
            t_dispatched = null;
            if (noted is not null)
            {
                token = noted.Token;
                if (pending.Equals(new ValueTask(noted, token))) return noted;
            }

            token = 0;
            return null;
        }

        /// <summary>Rent an operation that will parse its reply with <paramref name="handler"/>.</summary>
        /// <param name="handler">Parses the reply.</param>
        /// <param name="executor">Whose multiplexer an authentication fault is reported to.</param>
        internal static RespPayloadOperation<TResult> Rent(IRespHandler<TResult> handler, RespExecutorBase executor)
        {
            var operation = TryTake(TypedPool) ?? new RespPayloadOperation<TResult>();
            operation._handler = handler;
            operation._executor = executor;

            // as the untyped Rent: rented during a synchronous call, its continuations run on that thread
            SyncPump.Current?.OnRented(operation);
            return operation;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Everything the async tail used to do, in the same order: take the payload (which may recycle this
        /// instance, so the handler and executor are read first), translate an authentication fault, parse,
        /// and release the payload.
        /// </remarks>
        TResult IValueTaskSource<TResult>.GetResult(short token)
        {
            var handler = _handler!;
            var executor = _executor!;
            try
            {
                var response = GetResult(token);
                try
                {
                    return RespExecutor.Parse(handler, response);
                }
                finally
                {
                    response?.Release();
                }
            }
            catch (RedisServerException ex) when (RespExecutor.AuthFault(executor, ex) is { } authFault)
            {
                throw authFault;
            }
        }

        /// <inheritdoc/>
        /// <remarks>
        /// The UNGUARDED status, as the untyped interface gives. The public <c>GetStatus</c> refuses an operation
        /// that was never sent, to catch awaiting an unexecuted batch - but a send that failed before it was
        /// written (fail-fast while disconnected) is exactly that, already faulted, and a <c>ValueTask</c> asks
        /// its status before taking the result: binding to the guarded one replaced the real connection error
        /// with "this command has not been sent".
        /// </remarks>
        ValueTaskSourceStatus IValueTaskSource<TResult>.GetStatus(short token)
            => ((IValueTaskSource<RespPayload>)this).GetStatus(token);

        /// <inheritdoc/>
        /// <remarks>
        /// As a plain <see cref="System.Threading.Tasks.ValueTask"/>, for a command whose result is only "it succeeded": parsed and
        /// released exactly as the typed result is - an error reply still throws, and the payload is never
        /// leaked - and the value discarded. Without this, such commands awaited the typed task inside an async
        /// adapter, which boxed on every suspension.
        /// </remarks>
        void IValueTaskSource.GetResult(short token) => _ = ((IValueTaskSource<TResult>)this).GetResult(token);

        /// <inheritdoc/>
        ValueTaskSourceStatus IValueTaskSource.GetStatus(short token)
            => ((IValueTaskSource<RespPayload>)this).GetStatus(token);

        /// <inheritdoc/>
        protected override void OnReset()
        {
            _handler = null;
            _executor = null;
            base.OnReset();
        }

        /// <inheritdoc/>
        protected override void OnRecyclable() => TryGive(TypedPool, this);
    }
}
