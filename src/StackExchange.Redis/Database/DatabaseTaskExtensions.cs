using System.Threading.Tasks;
using StackExchange.Redis.Interfaces;

namespace StackExchange.Redis
{
    /// <summary>
    /// Serve a <see cref="Task"/>-returning surface - <see cref="IDatabaseAsync"/>'s shape - from the command groups,
    /// which return <see cref="ValueTask"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The other half of <see cref="RespContext.Blocking"/></b> for a library that keeps a legacy surface over
    /// the groups: <c>Blocking()</c> serves its synchronous methods, and this its task-returning ones. It is what
    /// <see cref="IDatabaseAsync"/>'s own methods use.
    /// </para>
    /// <para>
    /// <b>Why not <c>ValueTask.AsTask()</c>.</b> Two things the shipped surface has always done, and which a task
    /// from <c>AsTask()</c> cannot: it carries the database's async state (<see cref="Task.AsyncState"/> is fixed
    /// when a task is constructed, and <c>AsTask()</c> takes none), and a fault is marked observed as it faults, so
    /// a caller who drops the task - <c>_ = transaction.SomethingAsync(...)</c>, then a transaction that aborts -
    /// never sees <see cref="TaskScheduler.UnobservedTaskException"/>.
    /// </para>
    /// <para>
    /// <b>On the database rather than on the <see cref="ValueTask"/></b>, so the async state is the database's own
    /// - no caller has to find it - and so the method appears on this library's types rather than on every
    /// <see cref="ValueTask"/> in a program that imports this namespace.
    /// </para>
    /// </remarks>
    public static class DatabaseTaskExtensions
    {
        /// <summary>A task for a command group's result, as <paramref name="database"/>'s own methods return.</summary>
        /// <typeparam name="T">The result type.</typeparam>
        /// <param name="database">The database whose async state the task carries.</param>
        /// <param name="pending">The group method's result; consumed, so do not use it again.</param>
        /// <returns>The task.</returns>
        /// <example>
        /// <code>
        /// public Task&lt;long&gt; CountAsync(RedisKey key)
        ///     => _db.AsTask(_db.Contoso().CountAsync(key));
        /// </code>
        /// </example>
        public static Task<T> AsTask<T>(this IDatabaseAsync database, ValueTask<T> pending)
            => pending.AsTask(database.GetAsyncState(), CommandFlags.None);

        /// <inheritdoc cref="AsTask{T}(IDatabaseAsync, ValueTask{T})"/>
        /// <param name="database">The database whose async state the task carries.</param>
        /// <param name="pending">The group method's result; consumed, so do not use it again.</param>
        /// <param name="flags">
        /// The command's flags. Only fire-and-forget matters here: such a task carries no async state, as the
        /// shipped surface's never has.
        /// </param>
        public static Task<T> AsTask<T>(this IDatabaseAsync database, ValueTask<T> pending, CommandFlags flags)
            => pending.AsTask(database.GetAsyncState(), flags);

        /// <inheritdoc cref="AsTask{T}(IDatabaseAsync, ValueTask{T})"/>
        public static Task AsTask(this IDatabaseAsync database, ValueTask pending)
            => pending.AsTask(database.GetAsyncState(), CommandFlags.None);

        /// <inheritdoc cref="AsTask{T}(IDatabaseAsync, ValueTask{T}, CommandFlags)"/>
        public static Task AsTask(this IDatabaseAsync database, ValueTask pending, CommandFlags flags)
            => pending.AsTask(database.GetAsyncState(), flags);
    }
}
