using System.Threading.Tasks;

namespace StackExchange.Redis
{
    /// <summary>
    /// Represents a group of operations that will be sent to the server as a single unit,
    /// and processed on the server as a single unit. Transactions can also include constraints
    /// (implemented via <c>WATCH</c>), but note that constraint checking involves will (very briefly)
    /// block the connection, since the transaction cannot be correctly committed (<c>EXEC</c>),
    /// aborted (<c>DISCARD</c>) or not applied in the first place (<c>UNWATCH</c>) until the responses from
    /// the constraint checks have arrived.
    /// </summary>
    /// <remarks>
    /// <para>Note that on a cluster, it may be required that all keys involved in the transaction (including constraints) are in the same hash-slot.</para>
    /// <para><seealso href="https://redis.io/topics/transactions"/></para>
    /// </remarks>
    public interface ITransaction : IBatch, ITransactionAsync
    {
        /// <summary>
        /// Adds a precondition for this transaction.
        /// </summary>
        /// <param name="condition">The condition to add to the transaction.</param>
        // re-declared (rather than inherited from ITransactionAsync) so this interface's surface is
        // self-contained and unambiguous for existing callers
        new ConditionResult AddCondition(Condition condition);

        /// <summary>
        /// Execute the batch operation, sending all queued commands to the server.
        /// </summary>
        /// <param name="flags">The command flags to use.</param>
        bool Execute(CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Execute the batch operation, sending all queued commands to the server.
        /// </summary>
        /// <param name="flags">The command flags to use.</param>
        new Task<bool> ExecuteAsync(CommandFlags flags = CommandFlags.None);
    }
}
