using System;
using System.Threading.Tasks;

namespace StackExchange.Redis
{
    /// <summary>
    /// EXPERIMENTAL SPIKE. An <see cref="IDatabase"/> backed by the new context surface, one command at a
    /// time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The transition vehicle: eventually <c>GetDatabase()</c> returns one of these (under some less
    /// provisional name), so the existing interface keeps working while the commands behind it move to the
    /// new write path one at a time. Anything not yet moved throws <see cref="NotImplementedException"/>,
    /// which is loud, local, and impossible to mistake for working.
    /// </para>
    /// <para>
    /// <b>The unimplemented members are not written here - they are generated.</b> <c>[AutoDatabase]</c>
    /// emits every member of <see cref="IDatabase"/>/<see cref="IDatabaseAsync"/> that this class does not
    /// implement itself, funnelled through <c>Execute</c>/<c>ExecuteAsync</c> below. So this file does not
    /// grow a line when a command is added to the interface, and cannot drift out of step with it: the
    /// generated set is *by construction* "everything not in
    /// <c>TransitionalDatabase.Implemented.cs</c>". A hand-written NIE file would need a line per command
    /// and would silently miss new ones.
    /// </para>
    /// <para>
    /// That skip-what-is-implemented behaviour is new, and it is a correctness fix rather than a
    /// convenience: the generator emits <i>explicit</i> interface implementations, so a hand-written member
    /// does not collide with a generated one - both compile, and interface dispatch quietly prefers the
    /// generated throw.
    /// </para>
    /// </remarks>
    [AutoDatabase(WarnIfIncomplete = true)]
    internal sealed partial class TransitionalDatabase(RespDatabaseContext inner, IConnectionMultiplexer multiplexer, object? asyncState, IDatabase? fallback = null)
        : IDatabase
    {
        /// <inheritdoc/>
        public RespDatabaseContext Context => _inner;
        private readonly RespDatabaseContext _inner = inner;

        /// <summary>
        /// An old-surface database to forward not-yet-moved commands to, or <see langword="null"/> to throw.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>This is what lets the EXISTING test suite be the proof.</b> Given a fallback, an instance of
        /// this class is a drop-in <see cref="IDatabase"/> that happens to route the moved commands through
        /// the new write path and everything else through the old one - so <c>StringTests</c> runs
        /// unmodified against it and every assertion in it becomes an assertion about the new surface. The
        /// alternative is a parallel suite that re-states the same expectations and drifts.
        /// </para>
        /// <para>
        /// Deliberately <b>not</b> the default: without a fallback the throw is the point (see the type
        /// remarks), and a production transitional database that silently forwards would make "has this
        /// moved?" unanswerable. It is opt-in, and the only thing that opts in is a test harness.
        /// </para>
        /// </remarks>
        private readonly IDatabase? _fallback = fallback;

        /// <inheritdoc/>
        RespContext IRespTarget.Context => _inner.Raw;

        /// <summary>The async state carried by tasks this database produces.</summary>
        public object? AsyncState => asyncState;

        /// <inheritdoc/>
        public int Database => _inner.Database;

        /// <inheritdoc/>
        public IConnectionMultiplexer Multiplexer => multiplexer;

        // ---- [AutoDatabase] funnels ---------------------------------------------------------------------
        // Every member this class does not implement lands here. Normally there is no inner IDatabase to
        // forward to - that is the whole point - so the captured state is never invoked, and the throw
        // names the member so the message says which command still needs moving. A test harness can supply
        // a fallback, and then the capture is invoked against it exactly once; see _fallback.
        private TResult Execute<TState, TResult>(in TState state, AutoDatabaseSyncOperation<TState, TResult> operation)
            where TState : struct
            => _fallback is { } db ? operation(in state, db) : throw NotMoved<TState>();

        private void Execute<TState>(in TState state, AutoDatabaseSyncOperation<TState> operation)
            where TState : struct
        {
            if (_fallback is not { } db) throw NotMoved<TState>();
            operation(in state, db);
        }

        private Task<TResult> ExecuteAsync<TState, TResult>(in TState state, AutoDatabaseAsyncOperation<TState, TResult> operation)
            where TState : struct
            => _fallback is { } db ? operation(in state, db) : throw NotMoved<TState>();

        private Task ExecuteAsync<TState>(in TState state, AutoDatabaseAsyncOperation<TState> operation)
            where TState : struct
            => _fallback is { } db ? operation(in state, db) : throw NotMoved<TState>();

        /// <summary>The fallback, or a throw naming what is missing.</summary>
        private IDatabase Fallback<TState>() => _fallback ?? throw NotMoved<TState>();

        private static NotImplementedException NotMoved<TState>()
            => new($"This command has not yet moved to the RESP context surface (captured as '{typeof(TState).Name}').");

        // ---- the sync bridge ----------------------------------------------------------------------------

        /// <summary>Block for an asynchronous result, applying the multiplexer's timeout.</summary>
        /// <typeparam name="T">The result type.</typeparam>
        /// <param name="pending">The operation to wait for.</param>
        /// <remarks>
        /// <para>
        /// <b>Sync is deliberately deprioritised</b>, so this is the cheap version rather than the right
        /// one. A synchronously-completed result - notably a client-side cache hit - is taken directly and
        /// costs nothing. Anything else blocks on a <c>Task</c>, which is sync-over-async: the async path
        /// completes its task sources with <c>RunContinuationsAsynchronously</c> (see
        /// <c>ResultBox.cs</c>), so the completion needs a thread-pool thread while this one is blocked
        /// holding another. That is the failure mode SER307/SER308 exist to warn about.
        /// </para>
        /// <para>
        /// The proper fix is routing rather than waiting: <c>IRespExecutor</c> already has a synchronous
        /// <c>Send</c>, and <c>RespExecutor.Send</c> already uses it, so a context flag consulted by the
        /// one shared funnel would make every sync call complete inline and reduce this method to its fast
        /// path. Worth doing when sync stops being deprioritised - not before.
        /// </para>
        /// </remarks>
        private T Wait<T>(ValueTask<T> pending)
        {
            // spelled the same way as the result-less overload below, deliberately: .Result would also
            // consume (it calls IValueTaskSource<T>.GetResult(_token)), but only a reader who already
            // knows that can tell - and the rule is the same rule, so it should look the same
            if (pending.IsCompletedSuccessfully) return pending.GetAwaiter().GetResult();

            #pragma warning disable SER308 // Blocking on a task through the library's Wait helpers
            return multiplexer.Wait(pending.AsTask());
            #pragma warning restore SER308
        }

        /// <inheritdoc cref="Wait{T}(ValueTask{T})"/>
        /// <param name="pending">The operation to wait for.</param>
        /// <remarks>
        /// The result-less twin, for commands that go through the <c>SendAsync</c> overload with no
        /// <c>TResult</c>. Not used yet - added alongside the generic one deliberately, because the
        /// consumption rule below is the kind of thing that gets rediscovered the hard way.
        /// </remarks>
        private void Wait(ValueTask pending)
        {
            if (pending.IsCompletedSuccessfully)
            {
                // NOT a no-op, and not optional. A ValueTask backed by an IValueTaskSource must have its
                // result consumed exactly once: GetResult(_token) is what lets the source complete its
                // lifecycle and be reset or returned to its pool. Observing IsCompletedSuccessfully and
                // returning would abandon it - the pooled source is never released, and the next operation
                // to borrow it can see a stale token. There is no value to take here, which is precisely
                // why it looks droppable and is not.
                pending.GetAwaiter().GetResult();
                return;
            }

            #pragma warning disable SER308 // Blocking on a task through the library's Wait helpers
            multiplexer.Wait(pending.AsTask()); // AsTask consumes the source too, so the branches stay exclusive
            #pragma warning restore SER308
        }

        // ---- members the generator deliberately skips (see AutoDatabaseGenerator.SkipMethod) -------------
        // These take the fallback too, so a harness that supplies one gets a complete IDatabase rather than
        // one with holes in exactly the places a test suite reaches for scaffolding.
        public IBatch CreateBatch(object? asyncState = null)
            => Fallback<IBatch>().CreateBatch(asyncState);

        public ITransaction CreateTransaction(object? asyncState = null)
            => Fallback<ITransaction>().CreateTransaction(asyncState);

        ITransactionAsync IDatabaseAsync.CreateTransaction(object? asyncState) => CreateTransaction(asyncState);

        public bool IsConnected(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Fallback<RedisKey>().IsConnected(key, flags);

        public System.Net.EndPoint? IdentifyEndpoint(RedisKey key = default, CommandFlags flags = CommandFlags.None)
            => Fallback<RedisKey>().IdentifyEndpoint(key, flags);

        public Task<System.Net.EndPoint?> IdentifyEndpointAsync(RedisKey key = default, CommandFlags flags = CommandFlags.None)
            => Fallback<RedisKey>().IdentifyEndpointAsync(key, flags);

        /// <inheritdoc/>
        /// <remarks><inheritdoc cref="PingAsync" path="/remarks"/></remarks>
        public TimeSpan Ping(CommandFlags flags = CommandFlags.None)
            => Wait(_inner.PingMeasureAsync(flags));

        /// <inheritdoc/>
        /// <remarks>
        /// <para>
        /// <b>The number is measured from a slightly earlier instant than the shipped one, and that is the
        /// chosen behaviour rather than a limitation being tolerated.</b> <c>TimingProcessor</c> reads
        /// <c>TimerMessage.StartedWritingTimestamp</c>, stamped inside <c>WriteImpl</c>, so it times the
        /// server and excludes whatever the message spent queued; <c>PingMeasureAsync</c>'s handler starts
        /// its clock just before the send, so it counts the queue too.
        /// </para>
        /// <para>
        /// Identical on an idle connection, larger under a backlog - which is the case that decided it:
        /// <b>a caller waiting behind a backlog is waiting for the whole of it</b>, so the end-to-end time
        /// is the one they are actually experiencing, and a number that hid the queue would be reassuring
        /// at exactly the wrong moment. <see cref="IRedis.Ping"/> has always documented its result as "the
        /// observed latency", which is this reading rather than the other one.
        /// </para>
        /// </remarks>
        public Task<TimeSpan> PingAsync(CommandFlags flags = CommandFlags.None)
            => _inner.PingMeasureAsync(flags).AsTask();

        // the Wait family operates on caller-supplied Tasks, not server calls
        #pragma warning disable SER308 // Blocking on a task through the library's Wait helpers
        public bool TryWait(Task task) => task.Wait(multiplexer.TimeoutMilliseconds);

        public void Wait(Task task) => multiplexer.Wait(task);

        public T Wait<T>(Task<T> task) => multiplexer.Wait(task);

        public void WaitAll(params Task[] tasks) => multiplexer.WaitAll(tasks);
        #pragma warning restore SER308
    }
}
