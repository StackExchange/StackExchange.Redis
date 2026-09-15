using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using RESPite;

namespace StackExchange.Redis.Interpolated
{
    /// <summary>
    /// EXPERIMENTAL SPIKE. Transient state for a RESP operation: everything the writer needs in order to
    /// render a command. Supplied to an interpolated string handler as the receiver of the call.
    /// </summary>
    /// <remarks>
    /// <para>See <c>design/interpolated-resp-writer.md</c>.</para>
    /// <para>
    /// This is the same triple <see cref="TestHarness"/> already carries (command map, channel prefix, key
    /// prefix) plus the cancellation/routing state from the <c>marc/respite</c> spike's <c>RespContext</c>.
    /// Long term this replaces <see cref="MessageWriter"/>, whose constructor already takes
    /// (channel prefix, command map, target).
    /// </para>
    /// <para>
    /// Note the two prefixes reach the wire by different routes today: the channel prefix is writer state
    /// applied at write time, whereas the key prefix rides on the <see cref="RedisKey"/> itself, put there
    /// upstream by the <c>KeyPrefixed*</c> decorators. Applying BOTH here at write time is what makes those
    /// decorators unnecessary - but note a key may ALREADY carry its own prefix, so the two must compose
    /// (via <see cref="RedisKey.WithPrefix"/>) rather than conflict.
    /// </para>
    /// </remarks>
    [Experimental(Experiments.InterpolatedWriter, UrlFormat = Experiments.UrlFormat)]
    public readonly struct RespContext
    {
        internal RespContext(
            CommandMap? commandMap = null,
            RedisKey keyPrefix = default,
            RedisChannel channelPrefix = default,
            int database = 0,
            ServerType serverType = ServerType.Standalone,
            CancellationToken cancellationToken = default,
            IRespExecutor? executor = null,
            object? services = null)
        {
            _commandMap = commandMap;
            _keyPrefix = keyPrefix; // normalise to bytes ONCE; the conversion can allocate for a string-backed key
            Database = database;
            ServerType = serverType;
            CancellationToken = cancellationToken;
            Executor = executor;
            _services = channelPrefix.IsNull
                ? services
                : ServiceLink.Add(services, new ChannelPrefixService(channelPrefix));
        }

        /// <summary>Where commands composed from this context are sent; <c>null</c> if none is configured.</summary>
        /// <remarks>
        /// Behaviour composes here - a retrying or caching executor is a decorator around an inner one - while
        /// configuration composes on the context, via <see cref="WithExecutor"/>. They are not alternatives.
        /// </remarks>
        internal IRespExecutor? Executor { get; }

        private readonly object? _services;

        /// <summary>Carries a channel prefix in the service slot; a class, so the struct is not boxed loose.</summary>
        private sealed class ChannelPrefixService(RedisChannel channel)
        {
            internal RedisChannel Channel { get; } = channel;
        }

        /// <summary>Services in one slot, as a chain: a service plus whatever was already there.</summary>
        /// <remarks>
        /// <para>
        /// Prepending, so the most recently added wins by lookup order - which means "replace" needs no
        /// code at all, and neither does removal: setting a prefix back to <c>default</c> simply shadows
        /// the old one with an empty one. An array would be copied on every add; a link is one small
        /// allocation, and the chain is immutable so every context clone shares it.
        /// </para>
        /// <para>
        /// Allocated per context <i>configuration</i>, never per command - and only from the second service
        /// onwards, since a context with exactly one keeps the bare object and never sees this.
        /// </para>
        /// </remarks>
        private sealed class ServiceLink(object service, object tail) : IServiceProvider
        {
            public object? GetService(Type serviceType)
            {
                if (serviceType.IsInstanceOfType(service)) return service;

                return tail is IServiceProvider provider
                    ? provider.GetService(serviceType)
                    : serviceType.IsInstanceOfType(tail) ? tail : null;
            }

            internal static object Add(object? existing, object service)
                => existing is null ? service : new ServiceLink(service, existing);
        }

        /// <summary>
        /// Obtain a service attached to this context, if any.
        /// </summary>
        /// <typeparam name="T">The service type.</typeparam>
        /// <param name="service">The service, when found.</param>
        /// <remarks>
        /// One slot, which either <i>is</i> the requested service - the common case, a type test - or is an
        /// <see cref="IServiceProvider"/> able to supply services this context knows nothing about. Same
        /// shape as <c>RespReader</c>'s service slot, and for the same reason: it makes the context
        /// extensible <b>without new fields</b>, so a capability that arrives later costs no API change and
        /// no growth in the struct. A cache is simply the first such service.
        /// </remarks>
        public bool TryGetService<T>([NotNullWhen(true)] out T? service)
            where T : class
        {
            switch (_services)
            {
                case T typed:
                    service = typed;
                    return true;
                case IServiceProvider provider when provider.GetService(typeof(T)) is T resolved:
                    service = resolved;
                    return true;
                default:
                    service = null;
                    return false;
            }
        }

        /// <summary>
        /// Resolve a known command to the bytes this context would send, honouring the command map.
        /// </summary>
        /// <param name="command">The command to resolve.</param>
        /// <exception cref="RedisCommandException">If the command map disables it.</exception>
        internal ReadOnlySpan<byte> ResolveCommand(RedisCommand command)
        {
            var resp = CommandMap.GetResp(command);
            if (resp.IsEmpty) throw ExceptionFactory.CommandDisabled(command);
            return resp;
        }

        /// <summary>
        /// Resolve a command <b>name</b> to the bytes this context would send.
        /// </summary>
        /// <param name="name">The command name.</param>
        /// <param name="resp">The bytes to send, when the name is one this library knows.</param>
        /// <returns>
        /// <c>false</c> when the name is not a known command, in which case the caller frames it verbatim -
        /// no command map can affect it, because the map is built by walking the <c>RedisCommand</c> enum.
        /// </returns>
        /// <exception cref="RedisCommandException">If the name is known but the command map disables it.</exception>
        /// <remarks>
        /// One place, because there are four callers - the string constructor, a leading literal token,
        /// <see cref="RespCommand"/>, and the enum overload - and "parse, map, and throw if disabled" is
        /// exactly the sort of three-step rule that drifts when it is written out four times.
        /// </remarks>
        internal bool TryResolveCommand(ReadOnlySpan<char> name, out ReadOnlySpan<byte> resp)
        {
            if (RedisCommandMetadata.TryParseCI(name, out var parsed) && parsed != RedisCommand.UNKNOWN)
            {
                resp = ResolveCommand(parsed);
                return true;
            }

            resp = default;
            return false;
        }

        /// <summary>The client-side cache attached to this context, or <c>null</c> for none.</summary>
        /// <remarks>Convenience over <see cref="TryGetService{T}"/>; the cache is not a field.</remarks>
        public RespClientCache? Cache => TryGetService<RespClientCache>(out var cache) ? cache : null;

        /// <summary>The rendered-script registry attached to this context, or <c>null</c> for none.</summary>
        /// <remarks>
        /// Separate from <see cref="Cache"/> on purpose: this holds <i>requests</i> and never invalidates,
        /// where that holds <i>responses</i> and is invalidated constantly. Sharing a type would mean one
        /// of the two lying about its lifetime.
        /// </remarks>
        public RespScriptCache? ScriptCache => TryGetService<RespScriptCache>(out var scripts) ? scripts : null;

        private readonly CommandMap? _commandMap;

        /// <summary>
        /// Ask what the server that would receive <paramref name="command"/> can do; see
        /// <see cref="IRespServerFeatures"/> for why this is a service and not something the context knows.
        /// </summary>
        /// <param name="command">The command whose routing decides which server answers.</param>
        /// <param name="key">The key being addressed, or <c>default</c> when the command routes to none.</param>
        /// <param name="flags">The command's flags.</param>
        /// <param name="features">The best answer available; populated either way.</param>
        /// <returns><see langword="false"/> when nothing is known - including when no such service is present.</returns>
        internal bool TryGetFeatures(RedisCommand command, in RedisKey key, CommandFlags flags, out RedisFeatures features)
        {
            if (TryGetService<IRespServerFeatures>(out var probe))
            {
                // the key prefix is part of the CONTEXT here, not of the key, and it is applied at write
                // time - so the key a caller hands us is not yet the key the server will see, and in a
                // cluster those bytes are what pick the node. Compose first, exactly as AppendFormatted
                // will, or we would sample the version of a node that never sees this command. A null key
                // means "routes to no particular key" and stays null: prefixing it would invent one.
                if (_keyPrefix is { Length: > 0 } && !key.IsNull)
                {
                    var prefixed = RedisKey.WithPrefix(_keyPrefix, key);
                    return probe.TryGetFeatures(command, in prefixed, flags, out features);
                }

                return probe.TryGetFeatures(command, in key, flags, out features);
            }

            // no probe: a bare context, a test fake, a hand-wired executor. Nothing is known, and every
            // caller of this treats that as "use the spelling that works everywhere".
            features = default;
            return false;
        }

        /// <summary>
        /// The command map. Note this copes with <c>default(RespContext)</c>: a struct always has an implicit
        /// parameterless constructor that zeroes every field, and <c>new RespContext()</c> binds to THAT rather
        /// than to the all-optional-arguments constructor below - so no field may be assumed non-null.
        /// </summary>
        public CommandMap CommandMap => _commandMap ?? CommandMap.Default;

        private readonly byte[]? _keyPrefix;

        /// <summary>The key prefix applied to every <see cref="RedisKey"/> written through this context.</summary>
        public RedisKey KeyPrefix => _keyPrefix;

        /// <summary>The key prefix as raw bytes, so the writer never pays a conversion per command.</summary>
        internal ReadOnlySpan<byte> KeyPrefixSpan => _keyPrefix;

        /// <summary>The prefix applied to channels written through this context.</summary>
        /// <remarks>
        /// Held as a <b>service</b> rather than a field. As a field it was a <see cref="RedisChannel"/> -
        /// 16 bytes, a quarter of the whole context - carried on every copy for pub/sub's benefit alone,
        /// while every data-type group ignored it. Resolving it costs a type test, paid only by code that
        /// actually writes a channel. See design notes section 3.3.
        /// </remarks>
        public RedisChannel ChannelPrefix
            => TryGetService<ChannelPrefixService>(out var prefix) ? prefix.Channel : default;

        /// <summary>The database index; part of cache identity, and NOT part of the rendered frame.</summary>
        public int Database { get; }

        /// <summary>The server type; cluster slots are only computed when this is a cluster.</summary>
        public ServerType ServerType { get; }

        /// <summary>Cancellation for operations issued through this context.</summary>
        public CancellationToken CancellationToken { get; }

        /// <summary>A copy of this context with a different cancellation token.</summary>
        /// <param name="cancellationToken">The token to use.</param>
        public RespContext WithCancellationToken(CancellationToken cancellationToken)
            => new(CommandMap, KeyPrefix, ChannelPrefix, Database, ServerType, cancellationToken);

        /// <summary>A copy of this context targeting a different database.</summary>
        /// <param name="database">The database index.</param>
        public RespContext WithDatabase(int database)
            => new(CommandMap, KeyPrefix, default, database, ServerType, CancellationToken, Executor, _services);

        /// <summary>A copy of this context with a different server type.</summary>
        /// <param name="serverType">The server type.</param>
        public RespContext WithServerType(ServerType serverType)
            => new(CommandMap, KeyPrefix, default, Database, serverType, CancellationToken, Executor, _services);

        /// <summary>
        /// Returns a context whose keys are prefixed. This is what replaces wrapping the database in a
        /// <c>KeyPrefixedDatabase</c> decorator: the prefix is state on a value type, not a new object graph.
        /// Nested calls compose, matching the decorator's behaviour.
        /// </summary>
        public RespContext WithKeyPrefix(RedisKey keyPrefix)
            => new(
                CommandMap,
                _keyPrefix is null ? keyPrefix : RedisKey.WithPrefix(_keyPrefix, keyPrefix),
                default,
                Database,
                ServerType,
                CancellationToken,
                Executor,
                _services);

        /// <summary>A copy of this context with a different channel prefix.</summary>
        /// <param name="channelPrefix">The prefix to apply to channels.</param>
        public RespContext WithChannelPrefix(RedisChannel channelPrefix)
            // a null prefix shadows any earlier one with an empty service rather than removing it: the
            // chain stays append-only, and ChannelPrefix reads default from it either way
            => WithServices(ServiceLink.Add(_services, new ChannelPrefixService(channelPrefix)));

        /// <summary>A copy of this context that sends through <paramref name="executor"/>.</summary>
        /// <param name="executor">The executor to send through.</param>
        internal RespContext WithExecutor(IRespExecutor? executor)
            => new(CommandMap, _keyPrefix, default, Database, ServerType, CancellationToken, executor, _services);

        /// <summary>A copy of this context carrying <paramref name="services"/>.</summary>
        /// <param name="services">The service, or an <see cref="IServiceProvider"/>, or <c>null</c>.</param>
        public RespContext WithServices(object? services)
            => new(CommandMap, _keyPrefix, default, Database, ServerType, CancellationToken, Executor, services);

        /// <summary>
        /// A copy of this context carrying <paramref name="service"/> <i>in addition to</i> whatever it
        /// already has, rather than in place of it.
        /// </summary>
        /// <param name="service">The service to add.</param>
        /// <remarks>
        /// <see cref="WithServices"/> replaces the slot, which is right when the caller owns everything in
        /// it - but a context built up in stages does not: <c>.WithCache(x).WithServices(y)</c> silently
        /// loses the cache. Anything appending to a chain someone else started wants this instead.
        /// </remarks>
        internal RespContext WithAdditionalService(object service)
            => WithServices(ServiceLink.Add(_services, service));

        /// <summary>A copy of this context that consults <paramref name="cache"/>.</summary>
        /// <param name="cache">The cache to consult, or <c>null</c> for none.</param>
        /// <remarks>Sugar over <see cref="WithServices"/>; "a context with a cache" is just a context whose
        /// services include one.</remarks>
        public RespContext WithCache(RespClientCache? cache) => WithServices(cache);

        /// <summary>A copy of this context that renders each script only once.</summary>
        /// <param name="scripts">The registry to use, or <c>null</c> for none.</param>
        /// <remarks>Without one, a script's <c>SCRIPT LOAD</c> is rendered afresh on every call - correct,
        /// and wasteful for anything used more than once.</remarks>
        public RespContext WithScriptCache(RespScriptCache? scripts) => WithServices(scripts);

        /// <summary>
        /// Run an arbitrary command and return the raw reply - the escape hatch, for commands this library
        /// does not model.
        /// </summary>
        /// <param name="command">The command name; resolved through the command map like any other.</param>
        /// <param name="args">The arguments, each already known to be a key or a value.</param>
        /// <param name="flags">The command's flags.</param>
        /// <remarks>
        /// <para>
        /// The point of <see cref="RedisKeyOrValue"/> here is that it <b>keeps key-ness</b>, which the older
        /// <c>Execute(string, object[])</c> loses to boxing. So an ad-hoc command renders with correct key
        /// marks, which means it can take part in routing, invalidation and the client-side cache exactly as
        /// a modelled command does - the difference between plumbing a module library in and actually
        /// serving it.
        /// </para>
        /// <para>
        /// Not an <c>async</c> method: the handler is a <c>ref struct</c> and cannot cross an <c>await</c>,
        /// so composition finishes synchronously and only the reply is awaited.
        /// </para>
        /// </remarks>
        public ValueTask<RespResult> ExecuteAsync(
            string command,
            ReadOnlyMemory<RedisKeyOrValue> args,
            CommandFlags flags = CommandFlags.None)
        {
            var frame = Render(command, args.Span);
            return this.SendAsync(ref frame, flags, RespHandlers.Result);
        }

        /// <summary>Render an ad-hoc command, marking each argument as a key or a value.</summary>
        private RespFrame Render(string command, ReadOnlySpan<RedisKeyOrValue> args)
        {
            var handler = new RespCommandHandler(0, args.Length, this, command);
            try
            {
                foreach (var arg in args)
                {
                    // the key/value distinction is the whole reason this signature exists; losing it here
                    // would quietly cost routing and invalidation
                    if (arg.IsKey)
                    {
                        handler.AppendFormatted(arg.Key);
                    }
                    else
                    {
                        handler.AppendFormatted(arg.Value);
                    }
                }

                return handler.Complete();
            }
            catch
            {
                handler.Dispose(); // Complete did not happen, so the buffer is still ours
                throw;
            }
        }

        /// <summary>A context whose cached answers must be no older than <paramref name="maxAge"/>.</summary>
        /// <param name="maxAge">The oldest answer this caller will accept.</param>
        /// <remarks>
        /// Narrows, never widens: the cache's own <see cref="CachePolicy.TimeToLive"/> is a ceiling, and
        /// this cannot raise it. So a caller can ask for fresher, never for staler than the deployment
        /// allows.
        /// <para>
        /// This is the one knob that belongs on the context rather than on the policy, because freshness
        /// tolerance is a property of the call and not of the connection or the deployment - and it is the
        /// one that could not be added to <c>IDatabase</c> at all without a binary break.
        /// </para>
        /// </remarks>
        public RespContext WithMaxCacheAge(TimeSpan maxAge)
            => WithServices(ServiceLink.Add(_services, new MaxCacheAgeService(maxAge)));

        /// <summary>The caller's freshness requirement, if they stated one.</summary>
        internal long MaxCacheAgeTicks
            => TryGetService<MaxCacheAgeService>(out var service) ? service.Ticks : long.MaxValue;

        /// <summary>
        /// Render a command. The <c>""</c> argument passes THIS CONTEXT - the receiver of the call - into the
        /// handler's constructor; that is how the handler reaches the command map, the prefixes, and the
        /// server type.
        /// </summary>
        /// <remarks>
        /// A real Execute would go on to dispatch the frame; this spike stops at "the right bytes were
        /// rendered, and we know which arguments were keys".
        /// </remarks>
        /// <summary>
        /// Begin a command whose argument list is not fully known at the call site, for optional or
        /// contextual arguments:
        /// <code>
        /// var cmd = ctx.Compose($"{RedisCommand.SET} {key} {value}");
        /// if (withTtl) { cmd.AppendFormatted(RespLiterals.EX); cmd.AppendFormatted(ttl); }
        /// using var frame = ctx.Render(ref cmd);
        /// </code>
        /// </summary>
        /// <remarks>
        /// The argument count is then only known at <see cref="RespCommandHandler.Complete"/>, so the
        /// <c>*N</c> header is back-filled rather than written as a compile-time constant. Prefer the
        /// single-expression form for fixed-arity commands.
        /// <para>
        /// NOTE: the handler cannot be held by <c>using</c>, because a <c>using</c> variable cannot be passed
        /// by <c>ref</c> (CS1657). If the window between Compose and Execute can throw, use try/finally and
        /// call <see cref="RespCommandHandler.Dispose"/>. This is the same constraint
        /// <c>DefaultInterpolatedStringHandler</c> lives under, and accepted for the same reason - see
        /// design doc section 6.5, where the identical precedent covers abandoning the rented buffer when
        /// an interpolation throws.
        /// </para>
        /// </remarks>
        public RespCommandHandler Compose([InterpolatedStringHandlerArgument("")] ref RespCommandHandler handler)
            => handler;

        /// <summary>
        /// As <see cref="Compose(ref RespCommandHandler)"/>, but with the command supplied as a real
        /// argument rather than as the first hole:
        /// <code>
        /// var cmd = ctx.Compose(RedisCommand.SET, $"{key}{value}");
        /// </code>
        /// </summary>
        /// <remarks>
        /// <c>("", nameof(command))</c> passes the receiver <b>and</b> the command into the handler's
        /// constructor, which lets the command map be consulted before the buffer is rented.
        /// </remarks>
        internal RespCommandHandler Compose(
            RedisCommand command,
            [InterpolatedStringHandlerArgument("", nameof(command))] ref RespCommandHandler handler)
            => handler;

        /// <summary>
        /// Initialize a builder with no interpolated part at all, for a fully dynamic argument list:
        /// <code>
        /// var cmd = ctx.Compose(RedisCommand.DEL, keys.Length);
        /// foreach (var key in keys) cmd.AppendFormatted(key);
        /// using var frame = ctx.Render(ref cmd);
        /// </code>
        /// </summary>
        /// <param name="command">The command to issue.</param>
        /// <param name="argHint">Expected number of arguments, used only to size the initial rent.</param>
        internal RespCommandHandler Compose(RedisCommand command, int argHint = 0)
            => new(0, argHint < 0 ? 0 : argHint, this, command);

        /// <summary>
        /// As the <c>RedisCommand</c> overload, taking a command <b>name</b>. The name is speculatively
        /// parsed to a known command so aliasing and disabling still apply; anything unrecognised is framed
        /// verbatim, as <c>IDatabase.Execute(string, ...)</c> does.
        /// </summary>
        /// <param name="command">The command name to issue.</param>
        /// <param name="handler">The interpolated arguments.</param>
        public RespCommandHandler Compose(
            string command,
            [InterpolatedStringHandlerArgument("", nameof(command))] ref RespCommandHandler handler)
            => handler;

        /// <summary>As the <c>RedisCommand</c> overload, taking a command <b>name</b>.</summary>
        /// <param name="command">The command name to issue.</param>
        /// <param name="handler">The interpolated arguments.</param>
        public RespFrame Render(
            string command,
            [InterpolatedStringHandlerArgument("", nameof(command))] ref RespCommandHandler handler)
            => Render(ref handler);

        /// <summary>
        /// As <see cref="Render(ref RespCommandHandler)"/>, with the command as a real argument.
        /// </summary>
        internal RespFrame Render(
            RedisCommand command,
            [InterpolatedStringHandlerArgument("", nameof(command))] ref RespCommandHandler handler)
            => Render(ref handler);

        /// <summary>
        /// Render a command. The <c>""</c> argument passes THIS CONTEXT - the receiver of the call - into
        /// the handler's constructor; that is how the handler reaches the command map, the prefixes, and the
        /// server type.
        /// </summary>
        /// <remarks>
        /// <b>Renders; it does not send.</b> The name matters because <c>IDatabase.Execute</c> in this same
        /// library sends a command and returns its result - two opposite meanings for one verb would be a
        /// trap for every reader after the first. Dispatch is <c>Send</c>/<c>SendAsync</c>; this stops at
        /// "the right bytes were rendered, and we know which arguments were keys".
        /// </remarks>
        /// <param name="handler">The interpolated command and arguments.</param>
        /// <returns>The rendered frame, with routing and key metadata.</returns>
        public RespFrame Render([InterpolatedStringHandlerArgument("")] ref RespCommandHandler handler)
        {
            if (CancellationToken.IsCancellationRequested)
            {
                // the handler has already rented a buffer by the time we get here - it is built in the
                // CALLER's frame, before this method is entered - so cancelling has to hand it back
                handler.Dispose();
                CancellationToken.ThrowIfCancellationRequested();
            }

            return handler.Complete();
        }
    }
}
