using System.Diagnostics.CodeAnalysis;
using RESPite;
using StackExchange.Redis.Interfaces;

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
    /// <param name="database">The database to make retrying; this must not be a batch, a transaction, or
    /// an already-retrying database.</param>
    /// <param name="retryPolicy">
    /// The policy to apply; when <see langword="null"/> (the default), the policy configured for the
    /// underlying connection is used - <see cref="MultiGroupOptions.RetryPolicy"/> for a connection group,
    /// <see cref="ConfigurationOptions.RetryPolicy"/> for a single connection, else
    /// <see cref="RetryPolicy.Default"/>.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>Retry is a link in the dispatch chain, not a wrapper on the database.</b> This re-dispatches
    /// through the same executor decorator as <see cref="WithRetry(RespDatabaseContext, RetryPolicy?)"/>:
    /// what is replayed is the rendered command, so everything the database carries - the key prefix, the
    /// database index, an <c>asyncState</c>, the multi-group failover source - travels unchanged. It used
    /// to build a capture-and-replay wrapper, which replayed <i>method calls</i> and therefore had to
    /// refuse an <c>asyncState</c> it could not stamp; the re-dispatched form has no such limit.
    /// </para>
    /// <para>
    /// <b>A database this cannot see inside is handed back unchanged.</b> An external
    /// <see cref="IDatabaseAsync"/> implementation exposes no executor to decorate, and there is nothing
    /// to capture any more - so rather than failing, such a database is returned as it came, without
    /// retry.
    /// </para>
    /// </remarks>
    /// <exception cref="System.InvalidOperationException">If <paramref name="database"/> is a batch, a
    /// transaction, or already retrying.</exception>
    public static IDatabaseAsync WithRetry(this IDatabaseAsync database, RetryPolicy? retryPolicy = null)
    {
        if (database is null) throw new System.ArgumentNullException(nameof(database));

        if (database.Context is not { Raw: { Executor: not null } raw })
        {
            // nothing to decorate: an external implementation, or a context with no executor; see remarks
            return database;
        }

        // a batch or a transaction queues rather than dispatches, so there is nothing to retry yet; the
        // context overload below refuses nesting, which is the third historical rejection
        database.RejectFlags(DatabaseFeatureFlags.Batch | DatabaseFeatureFlags.Transaction);

        var context = new RespDatabaseContext(raw).WithRetry(retryPolicy);
        return new RedisDatabase(context, database.Multiplexer, database.GetAsyncState());
    }

    /// <summary>
    /// A context whose commands are retried when a transient fault says they can be.
    /// </summary>
    /// <param name="context">The context to wrap.</param>
    /// <param name="retryPolicy">
    /// The policy to apply; when omitted, the one configured on the context's multiplexer, as the database
    /// overload resolves it, and <see cref="RetryPolicy.Default"/> if there is none.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>A decorator on the executor, which is why this returns a context rather than a wrapper type.</b>
    /// <see cref="WithRetry(IDatabaseAsync, RetryPolicy?)"/> has to build a whole replaying
    /// <see cref="IDatabaseAsync"/>, because a method call is what it replays; here the thing replayed is
    /// a rendered frame, so retry is one link in the send chain and everything downstream of it - the
    /// groups, the cache, the key prefix - is untouched and unaware.
    /// </para>
    /// <para>
    /// <b>Synchronous sends retry too</b> - including every send through a <see cref="RespContext.Blocking"/>
    /// context made from the result - pausing between attempts by blocking the calling thread, which a
    /// synchronous caller has already agreed to. The retrying database (the overload above) still implements
    /// only <see cref="IDatabaseAsync"/>.
    /// </para>
    /// <para>
    /// <b>The policy is resolved from configuration</b> through <see cref="RespDatabaseContext.Multiplexer"/>,
    /// exactly as the database overload resolves it. It used to default to <see cref="RetryPolicy.Default"/>
    /// whatever was configured, because a context had no multiplexer to ask.
    /// </para>
    /// </remarks>
    /// <exception cref="System.InvalidOperationException">If the context is already retrying.</exception>
    public static RespDatabaseContext WithRetry(this RespDatabaseContext context, RetryPolicy? retryPolicy = null)
    {
        var raw = context.Raw;
        var inner = raw.Executor ?? throw new System.InvalidOperationException(
            "This context has no executor, so there is nothing to retry through.");

        // cannot nest retry: two loops would multiply the attempt counts rather than sharing them
        if (inner is RespRetryExecutor)
        {
            throw new System.InvalidOperationException(
                "This context is already retrying; a second policy would multiply the attempts rather than replace them.");
        }

        // the failover source comes from the executor chain rather than from an IDatabaseAsync: a
        // multi-group chain offers one through GetFailoverSource, and anything else honestly has none.
        // Fetched per use, never captured - see GetFailoverSource.
        return new RespDatabaseContext(
            raw.WithExecutor(new RespRetryExecutor(inner, retryPolicy ?? ResolveRetryPolicy(raw.Multiplexer), inner.GetFailoverSource())));
    }

    // IDatabaseAsync always exposes its multiplexer (via IRedisAsync), so the configured policy is reachable
    // without the caller having to thread it through; note IConnectionMultiplexer is a public interface that
    // callers may implement or mock, so every step here degrades to the default rather than assuming a type
    private static RetryPolicy ResolveRetryPolicy(IConnectionMultiplexer? multiplexer) => multiplexer switch
    {
        IConnectionGroup group => group.Options.RetryPolicy,
        IInternalConnectionMultiplexer muxer => muxer.RawConfig.RetryPolicy ?? RetryPolicy.Default,
        _ => RetryPolicy.Default,
    };
}
