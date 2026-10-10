using System;
using System.Threading.Tasks;
using StackExchange.Redis.Interfaces;

namespace StackExchange.Redis
{
    /// <summary>
    /// An <see cref="IBatch"/> whose commands are composed through the new context
    /// surface and sent as one contiguous run.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The whole type is the executor swap.</b> Every command member is inherited - a batch is a
    /// <see cref="RedisDatabase"/> over a context whose executor queues instead of sending, and
    /// nothing on the command path knows or cares which it is. Compare v3's <c>RedisBatch</c>, where batching
    /// lived in overrides of the message-writing internals and the type therefore had to inherit
    /// <c>RedisDatabase</c>'s ~6,000 lines to reach them. That inheritance is what pinned the old core in
    /// place; this one costs nothing, because <see cref="RedisDatabase"/> is a funnel rather than
    /// an implementation.
    /// </para>
    /// <para>
    /// <b>Adjacency is the executor's job, not this type's.</b> On the shipped surface a batch is the
    /// only way to get several commands written together; here it composes with the cache, the key
    /// prefix and retry without any of them being told, because it is an ordinary context.
    /// </para>
    /// </remarks>
    internal class RedisBatch : RedisDatabase, IBatch
    {
        /// <summary>Null for a transaction, which overrides <see cref="ExecuteCoreAsync"/> and sends its own way.</summary>
        private readonly RespOperationBatchExecutor? _executor;

        private protected RedisBatch(
            RespDatabaseContext batching,
            RespOperationBatchExecutor? executor,
            IConnectionMultiplexer multiplexer,
            object? asyncState)
            : base(batching, multiplexer, asyncState)
            => _executor = executor;

        /// <summary>Wrap a context so its commands queue rather than send.</summary>
        internal static RedisBatch CreateBatch(RespDatabaseContext source, IConnectionMultiplexer multiplexer, object? asyncState)
        {
            var raw = source.Raw;
            var inner = raw.Executor ?? throw new InvalidOperationException(
                "This context has no executor, so there is nothing to batch through.");

            var executor = new RespOperationBatchExecutor(inner, reusable: true); // as the shipped IBatch: see the parameter
            return new RedisBatch(new RespDatabaseContext(raw.WithExecutor(executor)), executor, multiplexer, asyncState);
        }

        /// <inheritdoc cref="RedisDatabase.CanScan"/>
        private protected sealed override bool CanScan => false;

        /// <inheritdoc/>
        private protected override DatabaseFeatureFlags OwnFeatures => DatabaseFeatureFlags.Batch;

        /// <inheritdoc/>
        /// <remarks>
        /// <b>Deliberately does not wait.</b> The shipped contract is "send what is queued", and each
        /// command's own task is what a caller awaits for its result - so blocking here would turn every
        /// batch into a synchronous round trip, which is the opposite of the point.
        /// </remarks>
        public void Execute() => _ = ExecuteCoreAsync();

        /// <summary>Send everything queued; the task completes when every queued command has its reply.</summary>
        private protected virtual Task ExecuteCoreAsync() => _executor!.ExecuteAsync();
    }
}
