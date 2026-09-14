using System;
using System.Threading.Tasks;

namespace StackExchange.Redis.Interpolated
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
    internal sealed partial class TransitionalDatabase(RespDatabase inner, IConnectionMultiplexer multiplexer, object? asyncState)
        : IDatabase
    {
        private readonly RespDatabase _inner = inner;

        /// <inheritdoc/>
        public RespContext Context => _inner.Context;

        /// <summary>The async state carried by tasks this database produces.</summary>
        public object? AsyncState => asyncState;

        /// <inheritdoc/>
        public int Database => Context.Database;

        /// <inheritdoc/>
        public IConnectionMultiplexer Multiplexer => multiplexer;

        // ---- [AutoDatabase] funnels ---------------------------------------------------------------------
        // Every member this class does not implement lands here. There is no inner IDatabase to replay
        // against - that is the whole point - so the captured state is never invoked, and the throw names
        // the member so the message says which command still needs moving.
        private TResult Execute<TState, TResult>(in TState state, AutoDatabaseSyncOperation<TState, TResult> operation)
            where TState : struct
            => throw NotMoved<TState>();

        private void Execute<TState>(in TState state, AutoDatabaseSyncOperation<TState> operation)
            where TState : struct
            => throw NotMoved<TState>();

        private Task<TResult> ExecuteAsync<TState, TResult>(in TState state, AutoDatabaseAsyncOperation<TState, TResult> operation)
            where TState : struct
            => throw NotMoved<TState>();

        private Task ExecuteAsync<TState>(in TState state, AutoDatabaseAsyncOperation<TState> operation)
            where TState : struct
            => throw NotMoved<TState>();

        private static NotImplementedException NotMoved<TState>()
            => new($"This command has not yet moved to the RESP context surface (captured as '{typeof(TState).Name}').");

        // ---- members the generator deliberately skips (see AutoDatabaseGenerator.SkipMethod) -------------
        public IBatch CreateBatch(object? asyncState = null) => throw new NotImplementedException();

        public ITransaction CreateTransaction(object? asyncState = null) => throw new NotImplementedException();

        ITransactionAsync IDatabaseAsync.CreateTransaction(object? asyncState) => CreateTransaction(asyncState);

        public bool IsConnected(RedisKey key, CommandFlags flags = CommandFlags.None) => throw new NotImplementedException();

        public System.Net.EndPoint? IdentifyEndpoint(RedisKey key = default, CommandFlags flags = CommandFlags.None)
            => throw new NotImplementedException();

        public Task<System.Net.EndPoint?> IdentifyEndpointAsync(RedisKey key = default, CommandFlags flags = CommandFlags.None)
            => throw new NotImplementedException();

        // the Wait family operates on caller-supplied Tasks, not server calls
        #pragma warning disable SER308 // Blocking on a task through the library's Wait helpers
        public bool TryWait(Task task) => task.Wait(multiplexer.TimeoutMilliseconds);

        public void Wait(Task task) => multiplexer.Wait(task);

        public T Wait<T>(Task<T> task) => multiplexer.Wait(task);

        public void WaitAll(params Task[] tasks) => multiplexer.WaitAll(tasks);
        #pragma warning restore SER308
    }
}
