using System.Diagnostics.CodeAnalysis;
using RESPite;

namespace StackExchange.Redis.Availability;

/// <summary>
///     Provides availability-related extension methods (such as
///     <see cref="WithRetry(IDatabaseAsync, RetryPolicy?)"/>) to database instances.
/// </summary>
// RS0026 warns that overloads carrying optional parameters can become ambiguous later. Not here: the two
// WithRetry overloads are told apart by their FIRST parameter - an IDatabaseAsync or a
// RespDatabaseContext - and neither has a default, so a call can only bind to one of them.
[SuppressMessage("ApiDesign", "RS0026:Do not add multiple overloads with optional parameters", Justification = "Overloads differ in a leading parameter that has no default; see the comment above")]
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
    public static IDatabaseAsync WithRetry(this IDatabaseAsync database, RetryPolicy? retryPolicy = null)
        => new RetryDatabase(database, retryPolicy ?? ResolveRetryPolicy(database));

    /// <summary>
    /// A context whose commands are retried when a transient fault says they can be.
    /// </summary>
    /// <param name="context">The context to wrap.</param>
    /// <param name="retryPolicy">The policy to apply; <see cref="RetryPolicy.Default"/> when omitted.</param>
    /// <remarks>
    /// <para>
    /// <b>A decorator on the executor, which is why this returns a context rather than a wrapper type.</b>
    /// <see cref="WithRetry(IDatabaseAsync, RetryPolicy?)"/> has to build a whole replaying
    /// <see cref="IDatabaseAsync"/>, because a method call is what it replays; here the thing replayed is
    /// a rendered frame, so retry is one link in the send chain and everything downstream of it - the
    /// groups, the cache, the key prefix - is untouched and unaware.
    /// </para>
    /// <para>
    /// <b>Asynchronous only.</b> A synchronous send through the result throws: every pause a retry takes
    /// is a <c>Task.Delay</c>, and the shipped retrying database refuses synchronous callers for the same
    /// reason - it implements <see cref="IDatabaseAsync"/> and not <see cref="IDatabase"/>.
    /// </para>
    /// <para>
    /// <b>The policy is not resolved from configuration here</b>, as the database overload resolves it
    /// from the multiplexer: a context deliberately does not carry one. Pass the policy, or attach it as a
    /// service when that is wired up.
    /// </para>
    /// </remarks>
    /// <exception cref="System.InvalidOperationException">If the context is already retrying.</exception>
    public static RespDatabaseContext WithRetry(this RespDatabaseContext context, RetryPolicy? retryPolicy = null)
    {
        var raw = context.Raw;
        var inner = raw.Executor ?? throw new System.InvalidOperationException(
            "This context has no executor, so there is nothing to retry through.");

        // cannot nest retry, as RetryDatabase.Validate refuses to wrap a retrying database: two loops
        // would multiply the attempt counts rather than sharing them
        if (inner is RespRetryExecutor)
        {
            throw new System.InvalidOperationException(
                "This context is already retrying; a second policy would multiply the attempts rather than replace them.");
        }

        return new RespDatabaseContext(
            raw.WithExecutor(new RespRetryExecutor(inner, retryPolicy ?? RetryPolicy.Default)));
    }

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
