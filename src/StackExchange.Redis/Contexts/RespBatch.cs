using System;
using System.Threading;
using System.Threading.Tasks;

namespace StackExchange.Redis
{
    /// <summary>
    /// A context whose commands are queued, and the handle that sends them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Two things rather than one, deliberately.</b> <see cref="Context"/> is an ordinary
    /// <see cref="RespDatabaseContext"/> - the groups, the cache and the key prefix neither know nor care
    /// that they are composing into a batch - and this object is the only thing that can send it. The
    /// shipped <see cref="IBatch"/> puts both on one type, which is why <c>Execute</c> sits alongside
    /// three hundred command members that have nothing to do with it.
    /// </para>
    /// <para>
    /// <b>Internal for now.</b> The shape of the public trigger is not settled - a <c>using</c> block that
    /// executes on leaving would fit an <c>await using</c> better than an explicit call, and that is a
    /// decision rather than a translation. This exists so the executor can be exercised.
    /// </para>
    /// </remarks>
    internal sealed class RespBatch : IDisposable
    {
        private readonly RespBatchExecutor _executor;
        private int _executed;

        internal RespBatch(RespDatabaseContext source)
        {
            var raw = source.Raw;
            var inner = raw.Executor ?? throw new InvalidOperationException(
                "This context has no executor, so there is nothing to batch through.");

            _executor = new RespBatchExecutor(inner);
            Context = new RespDatabaseContext(raw.WithExecutor(_executor));
        }

        /// <summary>Compose commands through this; each returns a task that completes on <see cref="ExecuteAsync"/>.</summary>
        internal RespDatabaseContext Context { get; }

        /// <summary>How many commands are queued.</summary>
        internal int Count => _executor.Count;

        /// <summary>Send everything queued.</summary>
        /// <param name="cancellationToken">Cancels the sends; only cancellation before each send is honoured today.</param>
        /// <remarks>
        /// The returned task completes when every queued command has its reply - which is <i>not</i> what
        /// a caller awaits to read one command's result: that is the task the command itself handed back.
        /// Awaiting this is how you wait for the whole batch.
        /// </remarks>
        internal Task ExecuteAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Exchange(ref _executed, 1);
            return _executor.ExecuteAsync(cancellationToken);
        }

        /// <summary>Discard anything still queued, faulting the callers waiting on it.</summary>
        /// <remarks><inheritdoc cref="RespBatchExecutor.Abandon" path="/remarks"/></remarks>
        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref _executed, 1, 0) == 0) _executor.Abandon();
        }
    }

    public static partial class RespSurface
    {
        /// <summary>Queue commands, and send them as one run.</summary>
        /// <param name="context">The context to batch over.</param>
        /// <remarks><inheritdoc cref="RespBatch" path="/remarks/para[2]"/></remarks>
        internal static RespBatch CreateBatch(this RespDatabaseContext context) => new(context);
    }
}
