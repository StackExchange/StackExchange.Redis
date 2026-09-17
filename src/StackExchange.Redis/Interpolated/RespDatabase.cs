using System;
using System.Diagnostics.CodeAnalysis;
using RESPite;

namespace StackExchange.Redis.Interpolated
{
    /// <summary>
    /// EXPERIMENTAL SPIKE. A minimal <see cref="IRespTarget"/>: a context, and nothing else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is what the context surface looks like without a live connection behind it - enough to exercise
    /// <c>target.Strings.SetAsync(...)</c> end to end, and to show that the whole public surface really is one
    /// member plus extension members. The connection-backed types (<c>RedisDatabase</c> and friends) throw
    /// from <see cref="IRespTarget.Context"/> for now: routing a rendered frame through the existing
    /// message pipeline is separate work, and there is no reason to hold this up behind it.
    /// </para>
    /// <para>
    /// Note what is <i>not</i> here: no command methods. <c>Set</c>, <c>Get</c> and everything after them
    /// are extension members over the context, so this type does not grow as the surface does - which is
    /// the entire argument of design notes section 9.4, made concrete.
    /// </para>
    /// </remarks>
    public sealed class RespDatabase : IRespKeyspaceTarget
    {
        /// <summary>Create a database over a context.</summary>
        /// <param name="context">The context commands are composed and sent through.</param>
        public RespDatabase(in RespContext context) => Context = context;

        /// <inheritdoc/>
        public RespContext Context { get; }

        /// <summary>A database for the same connection, with <paramref name="prefix"/> appended to whatever
        /// key prefix is already in force.</summary>
        /// <param name="prefix">The prefix to append; the result is <c>existing + this + key</c>.</param>
        /// <remarks>
        /// One context clone, with no per-method forwarding - the whole write half of
        /// <c>KeyPrefixedDatabase</c>.
        /// </remarks>
        public RespDatabase AppendKeyPrefix(RedisKey prefix) => new(Context.AppendKeyPrefix(prefix));

        /// <summary>A database bound to a different database index.</summary>
        /// <param name="database">The database index.</param>
        public RespDatabase WithDatabase(int database) => new(Context.WithDatabase(database));

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
