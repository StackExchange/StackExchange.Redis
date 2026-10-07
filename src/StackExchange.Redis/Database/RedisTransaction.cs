using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StackExchange.Redis.Interfaces;

namespace StackExchange.Redis
{
    /// <summary>
    /// An <see cref="ITransaction"/> over the new core: the queued commands, the
    /// preconditions, and <c>MULTI</c>/<c>EXEC</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A <see cref="RedisBatch"/> with a different executor and three extra members, which is
    /// what the shipped surface's <c>RedisTransaction : RedisBatch</c> is too - the difference being
    /// that neither this nor its base implements any commands, so the inheritance carries no weight.
    /// </para>
    /// <para>
    /// <b>Conditions are where the shapes genuinely part.</b> The shipped implementation evaluates them
    /// inside the write lock, pausing an enumerator with <c>Monitor</c> handshakes on result boxes
    /// because a blocked enumerator cannot hold the lock while the reader must make progress. Here the
    /// checks are simply awaited, and the pause is a boundary between two contiguous runs. See design
    /// notes §3c and §7r.
    /// </para>
    /// </remarks>
    internal sealed class RedisTransaction : RedisBatch, ITransaction, IInternalTransaction
    {
        /// <inheritdoc/>
        /// <remarks>
        /// Transaction rather than Batch, even though this derives from the batch: a caller asking
        /// "is this a transaction" wants the stronger answer, and <c>WithRetry</c> refuses both.
        /// </remarks>
        private protected sealed override DatabaseFeatureFlags OwnFeatures => DatabaseFeatureFlags.Transaction;

        private readonly RespTransactionExecutor _executor;
        private readonly List<ConditionResult> _conditions = [];

        private RedisTransaction(
            RespDatabaseContext queueing,
            RespTransactionExecutor executor,
            IConnectionMultiplexer multiplexer,
            object? asyncState)
            : base(queueing, executor: null, multiplexer, asyncState)
            => _executor = executor;

        /// <summary>Wrap a context so its commands run inside <c>MULTI</c>/<c>EXEC</c>.</summary>
        internal static RedisTransaction CreateTransaction(RespDatabaseContext source, IConnectionMultiplexer multiplexer, object? asyncState)
        {
            var raw = source.Raw;
            var inner = raw.Executor ?? throw new InvalidOperationException(
                "This context has no executor, so there is nothing to run a transaction through.");

            var executor = new RespTransactionExecutor(inner, raw);
            return new RedisTransaction(new RespDatabaseContext(raw.WithExecutor(executor)), executor, multiplexer, asyncState);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// The result is handed back <b>now</b> and filled in when the check is answered, which is what
        /// keeps "which condition failed?" answerable: a single overall bool from <c>Execute</c> cannot
        /// say that, and by the time it is false the conditions are gone.
        /// </remarks>
        public ConditionResult AddCondition(Condition condition)
        {
            if (condition is null) throw new ArgumentNullException(nameof(condition));
            condition.CheckCommands(Context.Raw.CommandMap);

            var result = new ConditionResult(condition);
            _conditions.Add(result);
            _executor.AddCondition(condition, result.SetSatisfied);
            return result;
        }

        /// <inheritdoc/>
        public bool WasWatchConflict => _executor.WasWatchConflict;

        /// <inheritdoc/>
        /// <remarks>
        /// <b>Without this the retry layer cannot retry a transaction at all.</b> It asks the transaction
        /// what a replay would do, and a transaction that does not answer is assumed to be
        /// <see cref="CommandFlags.CommandRetryNever"/> - so a transient fault the caller explicitly asked
        /// to ride out is rethrown instead, and their retry policy silently does nothing.
        /// </remarks>
        CommandFlags IInternalTransaction.GetAggregateRetryCategory() => _executor.AggregateRetryCategory;

        /// <inheritdoc/>
        /// <remarks>
        /// Sync-over-async, suppressed for the same reason and with the same reservations as
        /// <c>RedisDatabase.Wait</c>: sync is deprioritised on this surface, and the proper fix is
        /// routing rather than waiting.
        /// </remarks>
#pragma warning disable SER308 // Blocking on a task through the library's Wait helpers
        public bool Execute(CommandFlags flags = CommandFlags.None) => Wait(ExecuteAsync(flags));
#pragma warning restore SER308

        /// <inheritdoc/>
        /// <remarks>
        /// <b>The flags are not decoration.</b> A retrying caller passes the transaction's aggregate retry
        /// category here, and a fault the <c>EXEC</c> reports has to carry it back out - otherwise the
        /// failure is classified as never retryable and the retry policy silently does nothing.
        /// </remarks>
        public Task<bool> ExecuteAsync(CommandFlags flags = CommandFlags.None) => _executor.ExecuteAsync(flags);

        /// <inheritdoc cref="RedisBatch.ExecuteCoreAsync"/>
        /// <remarks>
        /// <see cref="IBatch.Execute"/> is inherited and fires this; the bool it discards is the one
        /// <see cref="Execute(CommandFlags)"/> returns. Sending a transaction and not looking at whether
        /// it ran is a strange thing to do, but it is what the inherited member means, so it does it
        /// rather than throwing.
        /// </remarks>
        private protected override Task ExecuteCoreAsync() => _executor.ExecuteAsync();
    }
}
