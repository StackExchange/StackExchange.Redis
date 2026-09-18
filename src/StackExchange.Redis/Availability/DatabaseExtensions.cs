using System.Diagnostics.CodeAnalysis;
using RESPite;

namespace StackExchange.Redis.Availability;

/// <summary>
///     Provides availability-related extension methods (such as <see cref="WithRetry"/>) to database instances.
/// </summary>
public static class DatabaseExtensions
{
    /// <summary>
    /// Automatically retry operations when connection failure occurs. This has deep integration with
    /// SE.Redis concepts, so can respond to server failover events, apply circuit-breaker rules, and
    /// respect command effect categorization.
    /// </summary>
    /// <param name="database">The database to wrap; this must not be a batch, a transaction, an
    /// already-retrying database, or a database carrying an <c>asyncState</c> (see remarks).</param>
    /// <param name="retryPolicy">
    /// The policy to apply; when <see langword="null"/> (the default), the policy configured for the
    /// underlying connection is used - <see cref="MultiGroupOptions.RetryPolicy"/> for a connection group,
    /// <see cref="ConfigurationOptions.RetryPolicy"/> for a single connection, else
    /// <see cref="RetryPolicy.Default"/>.
    /// </param>
    /// <remarks>
    /// <para><b>asyncState is not supported.</b> A database's <c>asyncState</c> is stamped onto the task
    /// produced by a single dispatch, but a retrying database hands back its own task spanning however
    /// many attempts the operation takes; the same is true of the per-operation tasks handed out by
    /// <see cref="IDatabaseAsync.CreateTransaction(object?)"/> on such a database. Rather than dropping
    /// the state silently, both refuse it: wrapping a database obtained via
    /// <c>GetDatabase(db, asyncState)</c> throws, as does supplying an <c>asyncState</c> when creating a
    /// transaction from a retrying database.</para>
    /// </remarks>
    /// <exception cref="System.InvalidOperationException">If <paramref name="database"/> is a batch, a
    /// transaction, already retrying, or carries an <c>asyncState</c>.</exception>
    [Experimental(Experiments.GeoRedundantFailover, UrlFormat = Experiments.UrlFormat)]
    public static IDatabaseAsync WithRetry(this IDatabaseAsync database, RetryPolicy? retryPolicy = null)
        => new RetryDatabase(database, retryPolicy ?? ResolveRetryPolicy(database));

    // IDatabaseAsync always exposes its multiplexer (via IRedisAsync), so the configured policy is reachable
    // without the caller having to thread it through; note IConnectionMultiplexer is a public interface that
    // callers may implement or mock, so every step here degrades to the default rather than assuming a type
    private static RetryPolicy ResolveRetryPolicy(IDatabaseAsync database) => database.Multiplexer switch
    {
        IConnectionGroup group => group.Options.RetryPolicy,
        IInternalConnectionMultiplexer muxer => muxer.RawConfig.RetryPolicy ?? RetryPolicy.Default,
        _ => RetryPolicy.Default,
    };
}
