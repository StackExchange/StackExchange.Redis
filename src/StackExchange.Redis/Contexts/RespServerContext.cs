using System;
using System.Threading.Tasks;
using StackExchange.Redis.Caching;

namespace StackExchange.Redis
{
    /// <summary>
    /// A context that knows it is pinned to <b>one server</b>: the entry point to the server-scoped
    /// command groups.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The twin of <see cref="RespDatabaseContext"/>, and the reason both exist: a context on its own is
    /// just routing and configuration, and cannot say whether <c>Strings</c> or <c>Keyspace</c> is the
    /// sensible thing to offer. These two say it, in the type - so <c>server.Strings</c> and
    /// <c>db.Keyspace</c> are compiler errors rather than runtime disappointments.
    /// </para>
    /// <para>
    /// <b>A struct, wrapping a context and adding semantics</b>, exactly as the groups themselves do.
    /// Reaching a group costs a copy of the context and nothing else; the accessors are generic over the
    /// target, so passing one does not box it.
    /// </para>
    /// </remarks>
    public readonly struct RespServerContext : IRespTarget
    {
        /// <summary>Create a server context over a context.</summary>
        /// <param name="context">The context commands are composed and sent through.</param>
        public RespServerContext(in RespContext context) => Raw = context;

        /// <summary>The shared plumbing this context wraps: key prefix, services, executor.</summary>
        /// <remarks>
        /// <b>Implementing <see cref="IRespTarget"/> and nothing narrower is the point.</b> A typed
        /// context genuinely does carry shared state, so the ad-hoc escape hatch reaches it; but it is
        /// NOT an <see cref="IRespKeyspaceTarget"/>, because the groups hang off the context directly and
        /// a context that was also a target would have to be its own <c>Context</c>.
        /// </remarks>
        public RespContext Raw { get; }

        // The scoping family returns THIS type rather than a bare context, and that is the whole reason it
        // is written out per context rather than shared: a naked context offers no groups, so a chain that
        // dropped back to one - db.Context.WithKeyPrefix("x:").Strings - would stop compiling halfway
        // along. Each is one line over the context underneath; the types are what carry the meaning.

        /// <summary>A copy of this context with <paramref name="services"/> added to the service bag.</summary>
        /// <param name="services">The service (or services) to add.</param>
        public RespServerContext WithServices(object? services) => new(Raw.WithServices(services));

        /// <summary>A copy of this context with client-side caching disabled.</summary>
        public RespServerContext WithoutCache() => new(Raw.WithoutCache());

        /// <summary>A copy of this context that will not serve a cached reply older than <paramref name="maxAge"/>.</summary>
        /// <param name="maxAge">The oldest reply this context will accept from the cache.</param>
        public RespServerContext WithMaxCacheAge(TimeSpan maxAge) => new(Raw.WithMaxCacheAge(maxAge));

        /// <summary>A copy of this context using <paramref name="scripts"/> to remember loaded scripts.</summary>
        /// <param name="scripts">The script cache, or <see langword="null"/> for none.</param>
        internal RespServerContext WithScriptCache(RespScriptCache? scripts) => new(Raw.WithScriptCache(scripts));

        /// <summary>A copy of this context whose channels carry <paramref name="channelPrefix"/>.</summary>
        /// <param name="channelPrefix">The prefix to append to whatever is already in force.</param>
        public RespServerContext AppendChannelPrefix(RedisChannel channelPrefix) => new(Raw.AppendChannelPrefix(channelPrefix));
    }
}
