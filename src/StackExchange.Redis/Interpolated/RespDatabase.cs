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
    /// <c>target.Strings.Set(...)</c> end to end, and to show that the whole public surface really is one
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
    [Experimental(Experiments.InterpolatedWriter, UrlFormat = Experiments.UrlFormat)]
    public sealed class RespDatabase : IRespTarget
    {
        /// <summary>Create a database over a context.</summary>
        /// <param name="context">The context commands are composed and sent through.</param>
        public RespDatabase(in RespContext context) => Context = context;

        /// <inheritdoc/>
        public RespContext Context { get; }

        /// <summary>A database for the same connection, with every key prefixed.</summary>
        /// <param name="prefix">The prefix to apply.</param>
        /// <remarks>
        /// One context clone, with no per-method forwarding - the whole write half of
        /// <c>KeyPrefixedDatabase</c>.
        /// </remarks>
        public RespDatabase WithKeyPrefix(RedisKey prefix) => new(Context.WithKeyPrefix(prefix));

        /// <summary>A database bound to a different database index.</summary>
        /// <param name="database">The database index.</param>
        public RespDatabase WithDatabase(int database) => new(Context.WithDatabase(database));
    }
}
