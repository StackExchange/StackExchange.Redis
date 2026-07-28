using System.Diagnostics.CodeAnalysis;
using RESPite;

namespace StackExchange.Redis.Availability;

/// <summary>
/// A database that transparently retries operations on transient faults, as created by
/// <see cref="DatabaseExtensions.WithRetry"/>. In addition to the standard asynchronous database
/// surface, it can create retryable transactions.
/// </summary>
[Experimental(Experiments.ActiveActive, UrlFormat = Experiments.UrlFormat)]
public interface IRetryDatabase : IDatabaseAsync
{
    /// <summary>
    /// Create a transaction whose execution is retried on transient faults, according to the
    /// governing <see cref="RetryPolicy"/>. The per-operation tasks handed out while building the
    /// transaction complete only after a non-faulting execution.
    /// </summary>
    /// <param name="asyncState">The async state to attach to the resulting operations.</param>
    ITransactionAsync CreateTransaction(object? asyncState = null);
}
