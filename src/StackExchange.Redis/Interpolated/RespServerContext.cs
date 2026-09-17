using System;

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
    public readonly struct RespServerContext : IRespServerTarget
    {
        /// <summary>Create a server context over a context.</summary>
        /// <param name="context">The context commands are composed and sent through.</param>
        public RespServerContext(in RespContext context) => Context = context;

        /// <inheritdoc/>
        public RespContext Context { get; }
    }
}
