using System.Threading.Tasks;

namespace StackExchange.Redis;

/// <summary>
/// Represents a group of operations that will be sent to the server as a single unit,
/// and processed on the server as a single unit, exposing only asynchronous completion.
/// This is the async-only counterpart to <see cref="ITransaction"/>, used where synchronous
/// execution is not offered - for example, a retrying database created via
/// <see cref="Availability.DatabaseExtensions.WithRetry"/>, where execution may inherently involve delays.
/// </summary>
/// <remarks>
/// <para>Transactions can also include constraints (implemented via <c>WATCH</c>).</para>
/// <para><seealso href="https://redis.io/topics/transactions"/></para>
/// </remarks>
public interface ITransactionAsync : IDatabaseAsync
{
    /// <summary>
    /// Adds a precondition for this transaction.
    /// </summary>
    /// <param name="condition">The condition to add to the transaction.</param>
    ConditionResult AddCondition(Condition condition);

    /// <summary>
    /// Execute the transaction, sending all queued commands to the server.
    /// </summary>
    /// <param name="flags">The command flags to use.</param>
    Task<bool> ExecuteAsync(CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Whether the transaction failed to commit because the *server* rejected the <c>EXEC</c>: every
    /// condition held, so <c>MULTI</c>/<c>EXEC</c> really was issued, but a key being watched on behalf of
    /// those conditions was modified by another connection in the meantime.
    /// </summary>
    /// <remarks>
    /// <para>A transaction that does not commit reports <c>false</c> from <c>Execute</c> for two quite
    /// different reasons, and this is what tells them apart:</para>
    /// <list type="bullet">
    /// <item><description>A condition was not satisfied, so the transaction was abandoned without ever
    /// issuing an <c>EXEC</c>. The value genuinely was not what was asserted, and re-running would assert
    /// the same thing again. This property is <c>false</c>, and the offending
    /// <see cref="ConditionResult.WasSatisfied"/> is also <c>false</c>.</description></item>
    /// <item><description>A watched key was changed by a <em>different</em> connection between the
    /// conditions being evaluated and the <c>EXEC</c> arriving. This property is <c>true</c>, every
    /// <see cref="ConditionResult.WasSatisfied"/> is <c>true</c>, and re-reading and trying again is the
    /// expected response - this is what the <c>WATCH</c>/<c>MULTI</c>/<c>EXEC</c> idiom is for.</description></item>
    /// </list>
    /// <para>Either way nothing was applied, and every queued operation's task is cancelled. This can only
    /// be <c>true</c> for a transaction that has conditions (without one there is no <c>WATCH</c>, so there
    /// is nothing to conflict over), and is <c>false</c> before the transaction has been executed.</para>
    /// </remarks>
    bool WasWatchConflict { get; }
}
