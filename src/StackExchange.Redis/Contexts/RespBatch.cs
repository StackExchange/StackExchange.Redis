using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using RESPite;

namespace StackExchange.Redis
{
    /// <summary>
    /// Commands that are queued, and sent together as one contiguous run when <see cref="ExecuteAsync"/>
    /// is called.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A target, not a database.</b> This implements <see cref="IRespKeyspaceTarget"/>, so every
    /// keyspace group is reached exactly as it is on a database - <c>batch.Strings.SetAsync(...)</c> - and
    /// returns the same task it would there. That task completes when the reply arrives, which is after
    /// <see cref="ExecuteAsync"/>; awaiting it <i>before</i> executing waits for something that has not
    /// been asked for.
    /// </para>
    /// <para>
    /// <b>Executed explicitly, never by leaving a scope.</b> A command that throws while the batch is being
    /// composed must not cause the half-built batch to be sent, and that is exactly what an
    /// execute-on-dispose would do. <see cref="Dispose"/> therefore <i>discards</i> anything not yet
    /// executed, faulting the tasks waiting on it.
    /// </para>
    /// <para>
    /// <b>A struct over a reference.</b> The queue lives in an executor this wraps, so copies are cheap and
    /// share everything; executing or discarding any copy affects all of them, and both are idempotent in
    /// the sense that matters - a discard after an execute does nothing. <c>default(RespBatch)</c> is not
    /// a batch, and says so.
    /// </para>
    /// <para>
    /// In a cluster the run is split by hash slot, and each slot's commands are written together to the
    /// node that owns it. No ordering is promised <i>between</i> slots.
    /// </para>
    /// </remarks>
    [Experimental(Experiments.Batching, UrlFormat = Experiments.UrlFormat)]
    public readonly struct RespBatch : IRespKeyspaceTarget, IDisposable
    {
        private readonly RespOperationBatchExecutor? _executor;
        private readonly RespDatabaseContext _context;

        internal RespBatch(RespDatabaseContext source)
        {
            var raw = RespBatching.Validate(source, "batch");
            _executor = new RespOperationBatchExecutor(raw.Executor!);
            _context = new RespDatabaseContext(raw.WithExecutor(_executor));
        }

        /// <summary>The context commands are composed through; each queues rather than sends.</summary>
        public RespDatabaseContext Context => _executor is null ? throw RespBatching.Default("batch") : _context;

        /// <inheritdoc/>
        RespContext IRespTarget.Context => Context.Raw;

        /// <summary>How many commands are queued.</summary>
        public int Count => _executor?.Count ?? 0;

        /// <summary>Send everything queued.</summary>
        /// <returns>A task that completes when the run has been <b>written</b>.</returns>
        /// <remarks>
        /// <b>Not when it has been answered.</b> Each command's own task is what carries its result; awaiting
        /// this says only that the batch has left. A batch executes once: a second call, or one after <see cref="Dispose"/>, throws.
        /// </remarks>
        public Task ExecuteAsync()
        {
            if (_executor is null) throw RespBatching.Default("batch");
            if (_executor.IsSent) throw RespBatching.AlreadySent("batch");
            return _executor.ExecuteAsync();
        }

        /// <summary>Discard anything still queued, faulting the tasks waiting on it.</summary>
        /// <remarks>Does nothing once the batch has executed.</remarks>
        public void Dispose() => _executor?.Abandon(RespBatching.Discarded("batch"));
    }

    /// <summary>
    /// Commands that are queued, and sent inside <c>MULTI</c>/<c>EXEC</c> when <see cref="ExecuteAsync"/>
    /// is called - optionally guarded by conditions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everything said of <see cref="RespBatch"/> holds here too: it is an <see cref="IRespKeyspaceTarget"/>,
    /// it executes only when asked, and <see cref="Dispose"/> discards. The difference is the return value:
    /// <see cref="ExecuteAsync"/> says whether the transaction <b>ran</b>, which is false when a condition
    /// did not hold or a watched key changed. When it did not run, each queued command's task is cancelled.
    /// </para>
    /// <para>
    /// <b>Conditions are the shipped <see cref="Condition"/> type.</b> They are rare enough that a
    /// new-surface spelling has not earned its keep yet; each one is <c>WATCH</c>ed and checked before the
    /// <c>MULTI</c> is sent, and its own verdict is reported through the <see cref="ConditionResult"/>
    /// <see cref="AddCondition"/> hands back.
    /// </para>
    /// <para>
    /// All the keys in a transaction must hash to one slot in a cluster, which is the server's rule rather
    /// than this library's.
    /// </para>
    /// </remarks>
    [Experimental(Experiments.Batching, UrlFormat = Experiments.UrlFormat)]
    public readonly struct RespTransaction : IRespKeyspaceTarget, IDisposable
    {
        private readonly RespTransactionExecutor? _executor;
        private readonly RespDatabaseContext _context;

        internal RespTransaction(RespDatabaseContext source)
        {
            var raw = RespBatching.Validate(source, "transaction");
            _executor = new RespTransactionExecutor(raw.Executor!, raw);
            _context = new RespDatabaseContext(raw.WithExecutor(_executor));
        }

        /// <summary>The context commands are composed through; each queues rather than sends.</summary>
        public RespDatabaseContext Context => _executor is null ? throw RespBatching.Default("transaction") : _context;

        /// <inheritdoc/>
        RespContext IRespTarget.Context => Context.Raw;

        /// <summary>How many commands are queued.</summary>
        public int Count => _executor?.Count ?? 0;

        /// <summary>
        /// Whether the conditions all held and the transaction was <i>still</i> aborted, because a watched
        /// key changed between the check and the <c>EXEC</c>.
        /// </summary>
        /// <remarks>
        /// The case worth retrying: a failed condition means the state is not what you wanted, where a watch
        /// conflict means it was, and somebody moved first.
        /// </remarks>
        public bool WasWatchConflict => _executor?.WasWatchConflict ?? false;

        /// <summary>Add a precondition that must hold for the transaction to run.</summary>
        /// <param name="condition">The condition; its key is watched, and it is checked before <c>MULTI</c>.</param>
        /// <returns>This condition's own verdict, filled in when the transaction executes.</returns>
        public ConditionResult AddCondition(Condition condition)
        {
            if (_executor is null) throw RespBatching.Default("transaction");
            if (condition is null) throw new ArgumentNullException(nameof(condition));
            condition.CheckCommands(_context.Raw.CommandMap);

            var result = new ConditionResult(condition);
            _executor.AddCondition(condition, result.SetSatisfied);
            return result;
        }

        /// <summary>Check the conditions, and if they hold send <c>MULTI</c>, the queued commands and <c>EXEC</c>.</summary>
        /// <returns>Whether the transaction ran.</returns>
        /// <remarks>A transaction executes once: a second call, or one after <see cref="Dispose"/>, throws.</remarks>
        public Task<bool> ExecuteAsync()
        {
            if (_executor is null) throw RespBatching.Default("transaction");
            if (_executor.IsSent) throw RespBatching.AlreadySent("transaction");
            return _executor.ExecuteAsync();
        }

        /// <summary>Discard anything still queued, faulting the tasks waiting on it.</summary>
        /// <remarks>Does nothing once the transaction has executed.</remarks>
        public void Dispose() => _executor?.Abandon(RespBatching.Discarded("transaction"));
    }

    /// <summary>Starting a batch or transaction from a keyspace target.</summary>
    /// <remarks>
    /// <para>
    /// <b><c>Begin</c>, not <c>Create</c></b>, because <see cref="IDatabase"/> already has
    /// <c>CreateBatch</c> and <c>CreateTransaction</c> as instance members, which always win over an
    /// extension - so <c>db.CreateBatch()</c> could only ever mean the shipped <see cref="IBatch"/>.
    /// </para>
    /// <para>
    /// Classic extension methods rather than an <c>extension</c> block, as a pair: one over the context
    /// itself, and one generic over any <see cref="IRespKeyspaceTarget"/> - constrained, so a struct target
    /// is not boxed to reach its context.
    /// </para>
    /// </remarks>
    public static class RespBatching
    {
        /// <summary>Queue commands, and send them as one run when executed.</summary>
        /// <param name="context">The context to batch over.</param>
        [Experimental(Experiments.Batching, UrlFormat = Experiments.UrlFormat)]
        public static RespBatch BeginBatch(this in RespDatabaseContext context) => new(context);

        /// <inheritdoc cref="BeginBatch(in RespDatabaseContext)"/>
        /// <typeparam name="TTarget">The kind of target.</typeparam>
        /// <param name="target">The target to batch over.</param>
        [Experimental(Experiments.Batching, UrlFormat = Experiments.UrlFormat)]
        public static RespBatch BeginBatch<TTarget>(this TTarget target) where TTarget : IRespKeyspaceTarget
            => new(target.Context);

        /// <summary>Queue commands, and send them inside <c>MULTI</c>/<c>EXEC</c> when executed.</summary>
        /// <param name="context">The context to run the transaction over.</param>
        [Experimental(Experiments.Batching, UrlFormat = Experiments.UrlFormat)]
        public static RespTransaction BeginTransaction(this in RespDatabaseContext context) => new(context);

        /// <inheritdoc cref="BeginTransaction(in RespDatabaseContext)"/>
        /// <typeparam name="TTarget">The kind of target.</typeparam>
        /// <param name="target">The target to run the transaction over.</param>
        [Experimental(Experiments.Batching, UrlFormat = Experiments.UrlFormat)]
        public static RespTransaction BeginTransaction<TTarget>(this TTarget target) where TTarget : IRespKeyspaceTarget
            => new(target.Context);

        /// <summary>The checks both kinds share: something to send through, able to send a run, not nested.</summary>
        internal static RespContext Validate(in RespDatabaseContext source, string kind)
        {
            var raw = source.Raw;
            var executor = raw.Executor ?? throw new InvalidOperationException(
                $"This context has no executor, so there is nothing to run a {kind} through.");
            if (executor.Accumulates) throw new NotSupportedException($"A {kind} cannot be started inside a batch or transaction.");
            if (!executor.CanWriteRuns) throw new NotSupportedException($"This executor cannot write a {kind} as one contiguous run.");
            return raw;
        }

        internal static InvalidOperationException Default(string kind)
            => new($"This {kind} was not started; use Begin{(kind == "batch" ? "Batch" : "Transaction")}.");

        internal static InvalidOperationException AlreadySent(string kind)
            => new($"This {kind} has already been executed or discarded.");

        internal static OperationCanceledException Discarded(string kind)
            => new($"The {kind} was discarded without being executed.");
    }
}
