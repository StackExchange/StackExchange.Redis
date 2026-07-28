using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using RESPite;

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
[Experimental(Experiments.ActiveActive, UrlFormat = Experiments.UrlFormat)]
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
}
