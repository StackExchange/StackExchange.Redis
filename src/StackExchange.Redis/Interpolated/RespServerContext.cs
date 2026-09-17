using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using StackExchange.Redis.Protocol;

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
    // RS0026 warns that overloads with optional parameters can become ambiguous. Not here: the two
    // ExecuteAsync overloads are told apart by their FIRST parameter - a database or a command name -
    // and neither has a default, so a call can only ever bind to one of them.
    [SuppressMessage("ApiDesign", "RS0026:Do not add multiple overloads with optional parameters", Justification = "Overloads differ in a leading parameter that has no default; see the comment above")]
    public readonly struct RespServerContext
    {
        /// <summary>Create a server context over a context.</summary>
        /// <param name="context">The context commands are composed and sent through.</param>
        public RespServerContext(in RespContext context) => Raw = context;

        /// <summary>The shared plumbing this context wraps: key prefix, services, executor.</summary>
        public RespContext Raw { get; }

        /// <summary>Run an arbitrary keyless command against this server and return the raw reply.</summary>
        /// <param name="command">The command name.</param>
        /// <param name="args">The arguments, each already known to be a key or a value.</param>
        /// <param name="flags">The command's flags.</param>
        /// <remarks>
        /// The escape hatch, diverging from the database one on purpose: this is keyless and node-pinned,
        /// so there is no database to assume; the overload below takes one when a command needs it.
        /// </remarks>
        public ValueTask<RespResult> ExecuteAsync(string command, ReadOnlyMemory<RedisKeyOrValue> args, CommandFlags flags = CommandFlags.None)
            => Raw.ExecuteAsync(command, args, flags);

        /// <summary>Run an arbitrary command against one database on this server.</summary>
        /// <param name="database">The database to run against; a server context has none of its own.</param>
        /// <param name="command">The command name.</param>
        /// <param name="args">The arguments, each already known to be a key or a value.</param>
        /// <param name="flags">The command's flags.</param>
        /// <remarks>
        /// The database comes first, as it does on <c>IServer.Execute(int?, string, ...)</c>, and it is
        /// required rather than defaulted: a server context cannot supply one, so a sentinel here would
        /// only be a way to not answer the question.
        /// </remarks>
        public ValueTask<RespResult> ExecuteAsync(int database, string command, ReadOnlyMemory<RedisKeyOrValue> args, CommandFlags flags = CommandFlags.None)
        {
            if (database < 0) throw new ArgumentOutOfRangeException(nameof(database), "A database is required; a server context has none of its own.");
            return Raw.WithDatabase(database).ExecuteAsync(command, args, flags);
        }

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
        public RespServerContext WithScriptCache(RespScriptCache? scripts) => new(Raw.WithScriptCache(scripts));

        /// <summary>A copy of this context whose channels carry <paramref name="channelPrefix"/>.</summary>
        /// <param name="channelPrefix">The prefix to append to whatever is already in force.</param>
        public RespServerContext AppendChannelPrefix(RedisChannel channelPrefix) => new(Raw.AppendChannelPrefix(channelPrefix));

        /// <inheritdoc cref="RespContext.Render(ref RespRequestBuilder)"/>
        /// <param name="request">The command, written as an interpolated string.</param>
        public RespRequestFrame Render([InterpolatedStringHandlerArgument("")] ref RespRequestBuilder request)
            => Raw.Render(ref request);
    }
}
