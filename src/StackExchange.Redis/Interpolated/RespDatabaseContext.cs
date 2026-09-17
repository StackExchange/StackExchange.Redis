using System;
using System.Diagnostics.CodeAnalysis;
using RESPite;

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
    public readonly struct RespDatabaseContext : IRespKeyspaceTarget
    {
        /// <summary>Create a database over a context.</summary>
        /// <param name="context">The context commands are composed and sent through.</param>
        public RespDatabaseContext(in RespContext context) => Context = context;

        /// <inheritdoc/>
        public RespContext Context { get; }

        /// <summary>A database for the same connection, with <paramref name="prefix"/> appended to whatever
        /// key prefix is already in force.</summary>
        /// <param name="prefix">The prefix to append; the result is <c>existing + this + key</c>.</param>
        /// <remarks>
        /// One context clone, with no per-method forwarding - the whole write half of
        /// <c>KeyPrefixedDatabase</c>.
        /// </remarks>
        public RespDatabaseContext AppendKeyPrefix(RedisKey prefix) => new(Context.AppendKeyPrefix(prefix));

        /// <summary>A database bound to a different database index.</summary>
        /// <param name="database">The database index.</param>
        public RespDatabaseContext WithDatabase(int database) => new(Context.WithDatabase(database));

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
