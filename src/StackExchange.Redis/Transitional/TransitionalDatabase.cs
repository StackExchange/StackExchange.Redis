using System;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Interfaces;

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
    /// new write path.
    /// </para>
    /// <para>
    /// <b>They have all moved.</b> Every member of <see cref="IDatabase"/>/<see cref="IDatabaseAsync"/> is
    /// implemented here against the context surface, and <c>[AutoDatabase]</c> has come off the class -
    /// for most of the port it generated the not-yet-moved members as explicit implementations that threw,
    /// with SER352 counting them down on every Release build. That count reached zero when <c>PUBLISH</c>
    /// moved, so the attribute generated nothing and was removed along with the funnels it fed.
    /// </para>
    /// <para>
    /// <b>Losing it is an improvement, not a regression.</b> A command added to the interface used to
    /// produce a generated throw and a warning; now it does not compile until it is implemented, which is
    /// the same guarantee <c>RedisDatabase</c> has always had and a better one than a tripwire. The
    /// generator itself stays - <c>RetryDatabase</c> and <c>MultiGroupDatabase</c> are capture-and-replay
    /// wrappers, which is what it is really for.
    /// </para>
    /// <para>
    /// <b>There is no fallback.</b> This once forwarded anything not yet moved to the v3 <c>RedisDatabase</c>, and
    /// then only batches and transactions over an executor that could not write a contiguous run (the
    /// <c>Message</c> shim). Both went with the old core: every command is implemented here, and creating a
    /// batch or transaction over an executor that cannot write one says so rather than forwarding.
    /// </para>
    /// </remarks>
    /// <remarks>
    /// <para>
    /// <b>Not sealed, because <see cref="TransitionalBatch"/> and <see cref="TransitionalTransaction"/>
    /// derive from it.</b> That looks like the shipped <c>RedisBatch : RedisDatabase</c> relationship
    /// this work exists to undo, and it is worth being precise about why it is fine here and not there.
    /// <c>RedisDatabase</c> is ~6,000 lines of command implementations, so inheriting it pins every one
    /// of them in place; this class implements nothing - it funnels to a
    /// <see cref="RespDatabaseContext"/>, and a batch is exactly the same funnel over a context whose
    /// executor queues. The inheritance buys ~504 members that would otherwise be forwarded by hand and
    /// costs nothing, because there is nothing behind it to be stuck with.
    /// </para>
    /// </remarks>
    internal partial class TransitionalDatabase(RespDatabaseContext inner, IConnectionMultiplexer multiplexer, object? asyncState)
        : IDatabase, IInternalDatabaseAsync
    {
        /// <inheritdoc/>
        public RespDatabaseContext Context => _inner;
        private readonly RespDatabaseContext _inner = inner;

        /// <inheritdoc/>
        RespContext IRespTarget.Context => _inner.Raw;

        /// <summary>The async state carried by tasks this database produces.</summary>
        public object? AsyncState => asyncState;

        /// <inheritdoc/>
        public int Database => _inner.Database;

        /// <summary>Whether a cursor-based scan can make progress through this database.</summary>
        /// <remarks>
        /// False for a batch or a transaction, and that is a property of <c>SCAN</c> rather than of those
        /// types: the cursor that asks for page two is read out of page one's <i>reply</i>, and neither
        /// has been sent when the enumeration starts. The shipped surface inherits <c>RedisDatabase</c>'s
        /// implementation here and so offers a scan that cannot advance; refusing is the better answer.
        /// </remarks>
        private protected virtual bool CanScan => true;

        private protected static NotSupportedException NoScanning() => new(
            "A cursor-based scan cannot run inside a batch or transaction: the cursor for every page after "
            + "the first is read from the previous page's reply, which has not been sent yet.");

        /// <inheritdoc/>
        public IConnectionMultiplexer Multiplexer => multiplexer;

        // ---- IInternalDatabaseAsync --------------------------------------------------------------------
        // NOT optional, and the reason is the failure mode: the extensions that read this interface fall
        // back to Unknown, null and CancellationToken.None when a database does not implement it, so a
        // database that quietly omits it does not fail - it just stops participating. GetNextFailover in
        // particular is how the retry layer learns that a failover happened, and a None token means retry
        // waits for one that never comes.
        //
        // Found by trying the GetDatabase swap: nothing failed, which is exactly the problem.

        /// <inheritdoc/>
        /// <remarks>The cluster flag is read from the context, which is where this surface keeps that fact.</remarks>
        DatabaseFeatureFlags IInternalDatabaseAsync.GetFeatures(out string name)
        {
            name = multiplexer?.ClientName ?? "";
            return (_inner.Raw.ServerType == ServerType.Cluster
                ? DatabaseFeatureFlags.Cluster
                : DatabaseFeatureFlags.None) | OwnFeatures;
        }

        /// <summary>What THIS wrapper is, as opposed to what the connection underneath can do.</summary>
        /// <remarks>
        /// <b>A batch has to say that it is one</b>, because callers ask: <c>WithRetry</c> refuses a batch
        /// or a transaction outright, since retrying a command that is queued inside one means retrying it
        /// outside the thing that gave it meaning. The shipped wrappers fold their own flag in exactly
        /// here, and these did not - which went unnoticed while <c>CreateBatch</c> still handed back the
        /// shipped type, and became visible the moment it stopped.
        /// </remarks>
        private protected virtual DatabaseFeatureFlags OwnFeatures => DatabaseFeatureFlags.None;

        /// <inheritdoc/>
        /// <remarks>
        /// This surface has no failover notion of its own; the databases that do - <c>RetryDatabase</c>,
        /// <c>MultiGroupDatabase</c> - sit above it and hand their own token down.
        /// </remarks>
        CancellationToken IInternalDatabaseAsync.GetNextFailover() => CancellationToken.None;

        // The [AutoDatabase] funnels used to be here - four of them, the landing place for every member
        // this class did not implement. They are gone with the attribute: there is no longer a member that
        // does not implement itself, so nothing was being generated and nothing reached them.

        /// <summary>The executor, which is the router; these three questions are all routing questions.</summary>
        /// <remarks>
        /// <b>No fallback, because the branch it replaced could not be reached.</b> All three used to ask
        /// the old database when this context had no executor - but a context with no executor cannot send
        /// anything at all, so such a database could answer "where would this go" and then never go there.
        /// The throw is the one a send gives, for the same reason.
        /// </remarks>
        private RespExecutorBase Router => _inner.Raw.Executor
            ?? throw new InvalidOperationException("No executor is configured for this context.");

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
        /// The proper fix is routing rather than waiting: <c>RespExecutorBase</c> already has a synchronous
        /// <c>Send</c>, and <c>RespExecutor.Send</c> already uses it, so a context flag consulted by the
        /// one shared funnel would make every sync call complete inline and reduce this method to its fast
        /// path. Worth doing when sync stops being deprioritised - not before.
        /// </para>
        /// </remarks>
        private T Wait<T>(ValueTask<T> pending) => TransitionalSync.Wait(pending, multiplexer, _inner.Raw.Executor);

        /// <summary>As <c>Wait</c>, for a call marked synchronous by <see cref="SyncCall.Begin"/>.</summary>
        private T Wait<T>(SyncCall call, ValueTask<T> pending) => TransitionalSync.Wait(call, pending, multiplexer, _inner.Raw.Executor);

        /// <summary>As <c>Wait</c>, for a call marked synchronous by <see cref="SyncCall.Begin"/>.</summary>
        private void Wait(SyncCall call, ValueTask pending) => TransitionalSync.Wait(call, pending, multiplexer);

        /// <inheritdoc cref="TransitionalSync.Wait(ValueTask, IConnectionMultiplexer)"/>
        /// <param name="pending">The operation to wait for.</param>
        private void Wait(ValueTask pending) => TransitionalSync.Wait(pending, multiplexer);

        // ---- members the generator deliberately skips (see AutoDatabaseGenerator.SkipMethod) -------------

        /// <inheritdoc/>
        /// <remarks>
        /// <b>The last two members to leave the fallback.</b> They were the gate on the whole deletion:
        /// <c>RedisBatch : RedisDatabase</c>, so as long as a batch had to come from the old surface,
        /// every one of <c>RedisDatabase</c>'s ~504 members had to stay to serve it.
        /// </remarks>
        public IBatch CreateBatch(object? asyncState = null)
        {
            // the runtime check rather than a virtual, exactly as RedisDatabase does it - and for a reason
            // worth keeping: IDatabaseAsync.CreateTransaction is an EXPLICIT implementation on this type,
            // so a derived class that merely declares its own public CreateTransaction is never reached
            // through the interface. One method that asks what it is beats two that can disagree.
            if (this is IBatch) throw new NotSupportedException("Nested batches are not supported");
            return CanWriteRuns
                ? TransitionalBatch.CreateBatch(_inner, multiplexer, asyncState ?? AsyncState)
                : throw new NotSupportedException("This executor cannot write a batch as one contiguous run.");
        }

        /// <summary>Whether a batch or transaction composed here could actually be written.</summary>
        /// <remarks>
        /// <b>Asked before building one, because the failure is otherwise silent until execution.</b> The
        /// new batch and transaction write their commands as a contiguous run, which needs a connection to
        /// write to - and the <c>Message</c> shim had none, reaching the server through the old pipeline
        /// instead. Composed over that, every batch failed with "cannot write a batch as one contiguous
        /// run", which was true but unhelpful while a perfectly good v3 implementation was available.
        /// <para>
        /// The shim went with the old core, so a production executor can always write a run; the question
        /// remains for executors that cannot, such as a test's fake, which are told so.
        /// </para>
        /// </remarks>
        private bool CanWriteRuns => _inner.Raw.Executor is { CanWriteRuns: true };

        /// <inheritdoc cref="CreateBatch"/>
        public ITransaction CreateTransaction(object? asyncState = null)
        {
            if (this is IBatch) throw new NotSupportedException("Nested transactions are not supported");
            // CanWriteTransactions, not CanWriteRuns: an executor can write a batch without being able to
            // hold a connection across MULTI/EXEC, and the old Message shim was exactly that
            return _inner.Raw.Executor is { CanWriteTransactions: true }
                ? TransitionalTransaction.CreateTransaction(_inner, multiplexer, asyncState ?? AsyncState)
                : throw new NotSupportedException("This executor cannot hold a connection across MULTI/EXEC.");
        }

        ITransactionAsync IDatabaseAsync.CreateTransaction(object? asyncState) => CreateTransaction(asyncState);

        /// <inheritdoc/>
        /// <remarks>
        /// Off the fallback: this asks the executor, because the executor is the router. No command is
        /// sent - the question is what routing <i>would</i> do with this key, which is the one thing the
        /// context cannot answer for itself. An executor with no routing says yes; see
        /// <c>RespExecutorBase.IsConnected</c>.
        /// </remarks>
        public bool IsConnected(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Router.IsConnected(in key, flags);

        /// <summary>
        /// <see cref="IsConnected"/> with the detail a boolean cannot carry; internal while the shape settles.
        /// </summary>
        /// <param name="key">The key whose routing is being asked about; null means "anywhere".</param>
        /// <param name="flags">The flags that would be used; they decide primary versus replica, and so which endpoint is being asked about.</param>
        internal RespConnectionState GetConnectionState(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Router.GetConnectionState(in key, flags);

        internal string RouterKindForTest => Router.GetType().Name;

        /// <inheritdoc/>
        /// <remarks>Off the fallback: the executor is the router, so it is the one that can answer.</remarks>
        public System.Net.EndPoint? IdentifyEndpoint(RedisKey key = default, CommandFlags flags = CommandFlags.None)
            => Wait(Router.IdentifyEndpointAsync(key, flags));

        /// <inheritdoc/>
        /// <remarks>Off the fallback; see the synchronous twin.</remarks>
        public Task<System.Net.EndPoint?> IdentifyEndpointAsync(RedisKey key = default, CommandFlags flags = CommandFlags.None)
            => Router.IdentifyEndpointAsync(key, flags).AsTask(AsyncState, flags);

        /// <inheritdoc/>
        /// <remarks><inheritdoc cref="PingAsync" path="/remarks"/></remarks>
        public TimeSpan Ping(CommandFlags flags = CommandFlags.None)
            => Wait(SyncCall.Begin(), _inner.PingMeasureAsync(flags));

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
            => _inner.PingMeasureAsync(flags).AsTask(AsyncState, flags);

        // the Wait family operates on caller-supplied Tasks, not server calls
        #pragma warning disable SER308 // Blocking on a task through the library's Wait helpers
        public bool TryWait(Task task) => task.Wait(multiplexer.TimeoutMilliseconds);

        public void Wait(Task task) => multiplexer.Wait(task);

        public T Wait<T>(Task<T> task) => multiplexer.Wait(task);

        public void WaitAll(params Task[] tasks) => multiplexer.WaitAll(tasks);
        #pragma warning restore SER308
    }
}
