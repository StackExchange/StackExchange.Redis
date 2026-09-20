using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace StackExchange.Redis
{
    /// <summary>
    /// EXPERIMENTAL SPIKE. An <see cref="ITransaction"/> over the new core: the queued commands, the
    /// preconditions, and <c>MULTI</c>/<c>EXEC</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A <see cref="TransitionalBatch"/> with a different executor and three extra members, which is
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
    internal sealed class TransitionalTransaction : TransitionalBatch, ITransaction
    {
        private readonly RespTransactionExecutor _executor;
        private readonly List<ConditionResult> _conditions = [];

        private TransitionalTransaction(
            RespDatabaseContext queueing,
            RespTransactionExecutor executor,
            IConnectionMultiplexer multiplexer,
            object? asyncState)
            : base(queueing, executor: null, multiplexer, asyncState)
            => _executor = executor;

        /// <summary>Wrap a context so its commands run inside <c>MULTI</c>/<c>EXEC</c>.</summary>
        internal static TransitionalTransaction CreateTransaction(RespDatabaseContext source, IConnectionMultiplexer multiplexer, object? asyncState)
        {
            var raw = source.Raw;
            var inner = raw.Executor ?? throw new InvalidOperationException(
                "This context has no executor, so there is nothing to run a transaction through.");

            var executor = new RespTransactionExecutor(inner, raw);
            return new TransitionalTransaction(new RespDatabaseContext(raw.WithExecutor(executor)), executor, multiplexer, asyncState);
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
        /// Sync-over-async, suppressed for the same reason and with the same reservations as
        /// <c>TransitionalDatabase.Wait</c>: sync is deprioritised on this surface, and the proper fix is
        /// routing rather than waiting.
        /// </remarks>
#pragma warning disable SER308 // Blocking on a task through the library's Wait helpers
        public bool Execute(CommandFlags flags = CommandFlags.None) => Wait(ExecuteAsync(flags));
#pragma warning restore SER308

        /// <inheritdoc/>
        public Task<bool> ExecuteAsync(CommandFlags flags = CommandFlags.None) => _executor.ExecuteAsync();

        /// <inheritdoc cref="TransitionalBatch.ExecuteCoreAsync"/>
        /// <remarks>
        /// <see cref="IBatch.Execute"/> is inherited and fires this; the bool it discards is the one
        /// <see cref="Execute(CommandFlags)"/> returns. Sending a transaction and not looking at whether
        /// it ran is a strange thing to do, but it is what the inherited member means, so it does it
        /// rather than throwing.
        /// </remarks>
        private protected override Task ExecuteCoreAsync() => _executor.ExecuteAsync();
    }
}
