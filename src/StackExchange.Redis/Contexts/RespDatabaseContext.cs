using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using RESPite;
using StackExchange.Redis.Caching;

namespace StackExchange.Redis
{
    /// <summary>
    /// A context that knows it is for a <b>keyspace</b>: the entry point to the database command groups.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A struct that wraps a context and adds semantics</b>, exactly as <see cref="RespStrings"/> and the
    /// other groups do - the difference being that this one says <i>which kind</i> of context it is. A naked
    /// <see cref="RespContext"/> carries no such claim, which is why nothing extends one: it cannot say
    /// whether the groups hanging off it make sense. This can, and its server twin
    /// <see cref="RespServerContext"/> says the opposite thing, so <c>server.Strings</c> and
    /// <c>db.Keyspace</c> fail to compile rather than being offered and then failing.
    /// </para>
    /// <para>
    /// Note what is <i>not</i> here: no command methods. <c>Set</c>, <c>Get</c> and everything after them
    /// are extension members over the context, so this type does not grow as the surface does - which is
    /// the entire argument of design notes section 9.4, made concrete.
    /// </para>
    /// </remarks>
    public readonly struct RespDatabaseContext : IRespTarget
    {
        /// <summary>Create a database over a context.</summary>
        /// <param name="context">The context commands are composed and sent through.</param>
        public RespDatabaseContext(RespContext context) => Raw = context;

        /// <summary>The shared plumbing this context wraps: key prefix, services, executor.</summary>
        /// <remarks>
        /// <para>
        /// <b>An internal field, reached from outside through an explicit cast.</b> It used to be a public
        /// property, which made <c>ctx.Raw</c> look like an ordinary part of the surface - and it is the
        /// opposite: a naked <see cref="RespContext"/> carries no semantics, so reaching for one is
        /// stepping out of the typed world on purpose. A cast is a thing you write deliberately and a
        /// reader notices, where a property is neither, and <b>explicit</b> rather than implicit for the
        /// same reason: it must never happen by accident during overload resolution.
        /// </para>
        /// <para>
        /// Inside the assembly it stays a plain field, so the group accessors that wrap it pay nothing and
        /// read as they did. <see cref="IRespTarget.Context"/> is implemented explicitly, because the
        /// <i>interface</i> is the extensibility seam - an extender holding an
        /// <see cref="IRespTarget"/> genuinely does need to get at the plumbing - and that is a different
        /// question from whether a concrete context should advertise it.
        /// </para>
        /// </remarks>
        internal readonly RespContext Raw;

        /// <summary>The shared plumbing this context wraps.</summary>
        /// <param name="context">The context to unwrap.</param>
        public static explicit operator RespContext(RespDatabaseContext context) => context.Raw;

        /// <inheritdoc/>
        RespContext IRespTarget.Context => Raw;

        /// <summary>The database index these commands run against.</summary>
        /// <remarks>
        /// <b>A database context's own fact</b>, which is why <see cref="RespServerContext"/> has no such
        /// member: a server is not database-scoped, and a property that always answered <c>-1</c> would be
        /// a worse answer than not offering one. It reads through to the shared state today; moving the
        /// field here outright is queued, and is what makes the number impossible to duplicate.
        /// </remarks>
        public int Database => Raw.Database;

        // The scoping family returns THIS type rather than a bare context, and that is the whole reason it
        // is written out per context rather than shared: a naked context offers no groups, so a chain that
        // dropped back to one - db.Context.WithKeyPrefix("x:").Strings - would stop compiling halfway
        // along. Each is one line over the context underneath; the types are what carry the meaning.

        /// <summary>A copy of this context with <paramref name="services"/> added to the service bag.</summary>
        /// <param name="services">The service (or services) to add.</param>
        public RespDatabaseContext WithServices(object? services) => new(Raw.WithServices(services));

        /// <summary>A copy of this context with client-side caching disabled.</summary>
        public RespDatabaseContext WithoutCache() => new(Raw.WithoutCache());

        /// <summary>A copy of this context that will not serve a cached reply older than <paramref name="maxAge"/>.</summary>
        /// <param name="maxAge">The oldest reply this context will accept from the cache.</param>
        public RespDatabaseContext WithMaxCacheAge(TimeSpan maxAge) => new(Raw.WithMaxCacheAge(maxAge));

        /// <summary>A copy of this context using <paramref name="scripts"/> to remember loaded scripts.</summary>
        /// <param name="scripts">The script cache, or <see langword="null"/> for none.</param>
        internal RespDatabaseContext WithScriptCache(RespScriptCache? scripts) => new(Raw.WithScriptCache(scripts));

        /// <summary>A copy of this context whose channels carry <paramref name="channelPrefix"/>.</summary>
        /// <param name="channelPrefix">The prefix to append to whatever is already in force.</param>
        public RespDatabaseContext AppendChannelPrefix(RedisChannel channelPrefix) => new(Raw.AppendChannelPrefix(channelPrefix));

        /// <summary>A database for the same connection, with <paramref name="prefix"/> appended to whatever
        /// key prefix is already in force.</summary>
        /// <param name="prefix">The prefix to append; the result is <c>existing + this + key</c>.</param>
        /// <remarks>
        /// One context clone, with no per-method forwarding - the whole write half of
        /// <c>KeyPrefixedDatabase</c>.
        /// </remarks>
        public RespDatabaseContext AppendKeyPrefix(RedisKey prefix) => new(Raw.AppendKeyPrefix(prefix));

        /// <summary>A database bound to a different database index.</summary>
        /// <param name="database">The database index.</param>
        public RespDatabaseContext WithDatabase(int database) => new(Raw.WithDatabase(database));

        /// <summary>
        /// This database as an <see cref="IDatabase"/>, for handing to code written against the existing
        /// interface.
        /// </summary>
        /// <param name="multiplexer">The multiplexer to report, and whose timeout the blocking members use.</param>
        /// <param name="asyncState">The async state to carry on tasks this database produces.</param>
        /// <remarks>
        /// <para>
        /// The return trip. <c>IDatabase.Context</c> already goes the other way, so with this the two
        /// surfaces interoperate in both directions and neither is a one-way door: new code can take a
        /// context and still hand an <see cref="IDatabase"/> to a library that wants one.
        /// </para>
        /// <para>
        /// Commands that have not yet moved to the context surface throw from the returned instance - it is
        /// a <i>transitional</i> database, and SER352 counts what is missing on every Release build. This is
        /// not the route by which the interface eventually gets its new implementation; that happens when
        /// <c>GetDatabase()</c> returns one directly.
        /// </para>
        /// <para>
        /// The concrete type stays <c>internal</c>: the contract here is <see cref="IDatabase"/>, which is
        /// the whole point, and keeping it that way means the transitional type can be renamed, replaced or
        /// deleted without a public API change.
        /// </para>
        /// </remarks>
        public IDatabase AsDatabase(IConnectionMultiplexer multiplexer, object? asyncState = null)
        {
            if (multiplexer is null) throw new ArgumentNullException(nameof(multiplexer));
            return new TransitionalDatabase(this, multiplexer, asyncState);
        }
    }
}
