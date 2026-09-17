using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using RESPite;
using StackExchange.Redis.Protocol;

namespace StackExchange.Redis
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
    public readonly struct RespContext
    {
        internal RespContext(
            CommandMap? commandMap = null,
            RedisKey keyPrefix = default,
            RedisChannel channelPrefix = default,
            int database = 0,
            ServerType serverType = ServerType.Standalone,
            IRespExecutor? executor = null,
            object? services = null)
        {
            _commandMap = commandMap;
            _keyPrefix = keyPrefix; // normalise to bytes ONCE; the conversion can allocate for a string-backed key
            _database = database;
            ServerType = serverType;
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

        /// <summary>
        /// An entry meaning "explicitly nothing of this type", so that a context can turn a service off
        /// without discarding the rest of the chain.
        /// </summary>
        /// <remarks>
        /// The chain is prepend-and-shadow, which gives replacement for free but leaves no way to say
        /// <i>none</i> - and <c>WithCache(null)</c> has to mean something. Matching is by exact type, the
        /// type whose lookup it answers, so a veto is invisible to any other request.
        /// </remarks>
        private sealed class ServiceVeto(Type serviceType)
        {
            internal Type ServiceType { get; } = serviceType;
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
                if (TryMatch(service, serviceType, out var match)) return match;

                return tail is IServiceProvider provider
                    ? provider.GetService(serviceType)
                    : TryMatch(tail, serviceType, out match) ? match : null;
            }

            /// <summary>Whether one entry answers a request - with the service, or with nothing for a veto.</summary>
            private static bool TryMatch(object candidate, Type serviceType, out object? match)
            {
                if (candidate is ServiceVeto veto)
                {
                    // a veto answers its own type and stops the walk there; anything else carries on past
                    match = null;
                    return veto.ServiceType == serviceType;
                }

                match = serviceType.IsInstanceOfType(candidate) ? candidate : null;
                return match is not null;
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
                // a lone ServiceVeto is a "none" and nothing else, so it falls through to not-found rather
                // than being handed back as a service
                case not ServiceVeto and T typed:
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
            => TryResolveCommand(name, out resp, out _);

        /// <inheritdoc cref="TryResolveCommand(ReadOnlySpan{char}, out ReadOnlySpan{byte})"/>
        /// <param name="name">The command name to resolve.</param>
        /// <param name="resp">The mapped command, already framed.</param>
        /// <param name="command">The command it resolved to, for callers that need its identity and not
        /// just its bytes.</param>
        internal bool TryResolveCommand(ReadOnlySpan<char> name, out ReadOnlySpan<byte> resp, out RedisCommand command)
        {
            if (RedisCommandMetadata.TryParseCI(name, out var parsed) && parsed != RedisCommand.UNKNOWN)
            {
                resp = ResolveCommand(parsed);
                command = parsed;
                return true;
            }

            resp = default;
            command = RedisCommand.UNKNOWN;
            return false;
        }

        /// <summary>The client-side cache attached to this context, or <c>null</c> for none.</summary>
        /// <remarks>Convenience over <see cref="TryGetService{T}"/>; the cache is not a field.</remarks>
        internal RespClientCache? Cache => TryGetService<RespClientCache>(out var cache) ? cache : null;

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
        /// <remarks>
        /// <b>The executor answers this whenever there is one</b>, because the executor is what actually
        /// routes: the cache keys on <c>executor.Database</c> and the message is built with it. Storing a
        /// second copy here and trusting it is what made <see cref="WithDatabase"/> wrong once already -
        /// it changed this value and not the executor's, so the call read database 0 and reported 1, with
        /// nothing inconsistent to see because the cache agreed with the executor. Deferring means the two
        /// cannot disagree; the field below is only what a context says before it has an executor at all.
        /// </remarks>
        public int Database => Executor?.Database ?? _database;

        private readonly int _database;

        /// <summary>The server type; cluster slots are only computed when this is a cluster.</summary>
        public ServerType ServerType { get; }

        /// <summary>A copy of this context targeting a different database.</summary>
        /// <param name="database">The database index.</param>
        /// <remarks>
        /// <para>
        /// <b>The executor moves too, and that is the whole method.</b> The database is not part of the
        /// rendered frame - no <c>SELECT</c> is written - so where a request actually lands is decided by
        /// the executor, not by this property. Changing only the property produced a context that
        /// <i>reported</i> database 1 and <i>read</i> database 0: a wrong answer with nothing to see, and
        /// the cache agreed with the executor, so it was not even inconsistent with itself.
        /// </para>
        /// <para>
        /// An executor that cannot be re-pointed says so rather than being carried along silently. The
        /// only one in this library can - it is the message pipeline, and a database is one field of the
        /// message - so this throws for fakes and for anything a third party supplies that does not
        /// already run against <paramref name="database"/>.
        /// </para>
        /// </remarks>
        /// <exception cref="NotSupportedException">
        /// The context has an executor that runs against a different database and cannot be re-pointed.
        /// </exception>
        public RespContext WithDatabase(int database)
        {
            var executor = Executor switch
            {
                null => null,                                        // nothing to route yet; WithExecutor comes later
                RespMessageExecutor message => message.WithDatabase(database),
                { } other when other.Database == database => other,  // already there; nothing to do
                { } other => throw new NotSupportedException(
                    $"This context's executor ({other.GetType().Name}) runs against database {other.Database} and cannot be re-pointed at database {database}."),
            };

            return new(CommandMap, KeyPrefix, default, database, ServerType, executor, _services);
        }

        /// <summary>
        /// Returns a context whose keys carry <paramref name="keyPrefix"/>, <b>appended to</b> any prefix
        /// already in force. This is what replaces wrapping the database in a <c>KeyPrefixedDatabase</c>
        /// decorator: the prefix is state on a value type, not a new object graph.
        /// </summary>
        /// <param name="keyPrefix">The prefix to append; the result is <c>existing + this + key</c>.</param>
        /// <remarks>
        /// <para>
        /// Named <c>Append</c> rather than <c>With</c> because the two read as opposites and only one of them
        /// is true: <c>With</c> says the result differs in this respect, which invites "so the second call
        /// wins". It does not - <c>AppendKeyPrefix("a").AppendKeyPrefix("b")</c> sends key <c>k</c> as
        /// <c>abk</c>, and there is deliberately no way back to <c>k</c>. That matches nesting the
        /// <c>KeyPrefixed*</c> decorators, and <see cref="KeyspaceIsolation.DatabaseExtensions.WithKeyPrefix"/>,
        /// which folds the two prefixes and re-wraps the inner database.
        /// </para>
        /// <para>
        /// A null or empty prefix appends nothing, rather than resetting. The shipped
        /// <see cref="KeyspaceIsolation.DatabaseExtensions.WithKeyPrefix"/> throws on null instead, which is
        /// right there and not here: there the prefix is the entire point of the call, so null is a caller
        /// error; here it is one setting among several on a context, where "adds nothing" is well defined.
        /// </para>
        /// </remarks>
        public RespContext AppendKeyPrefix(RedisKey keyPrefix)
            => new(
                CommandMap,
                _keyPrefix is null ? keyPrefix : RedisKey.WithPrefix(_keyPrefix, keyPrefix),
                default,
                Database,
                ServerType,
                Executor,
                _services);

        /// <summary>
        /// Returns a context whose channels carry <paramref name="channelPrefix"/>, <b>appended to</b> any
        /// prefix already in force - exactly as <see cref="AppendKeyPrefix"/> does, and as nesting the
        /// <c>KeyPrefixed*</c> decorators does.
        /// </summary>
        /// <param name="channelPrefix">The prefix to apply to channels, appended to any already in force.</param>
        /// <remarks>
        /// Composing rather than replacing, for the reason given on <see cref="WithServices"/>: a context is
        /// handed down through code that does not know what its caller already applied. If this assigned,
        /// a library reaching for its own channel namespace would silently cancel the tenant isolation its
        /// caller established - and there is nothing to see afterwards, because the frame is well-formed and
        /// goes to the wrong channel. For the same reason a null prefix is a no-op rather than a reset:
        /// <b>there is deliberately no way to escape a prefix already in force</b>, which matches the key
        /// prefix (you cannot un-prefix a <see cref="RedisKey"/>) and the decorators (you cannot unwrap one).
        /// </remarks>
        public RespContext AppendChannelPrefix(RedisChannel channelPrefix)
        {
            // nothing to add. Note this is an allocation saving, NOT what makes null a no-op: composing
            // nothing onto the existing bytes already yields the existing bytes (verified by mutation)
            if (channelPrefix.IsNull) return this;

            var existing = ChannelPrefix;
            if (!existing.IsNull)
            {
                // note the mode follows the incoming prefix; only the bytes reach the wire, since the writer
                // prepends them to a channel that carries its own mode
                channelPrefix = new RedisChannel(
                    RedisKey.ConcatenateBytes((byte[]?)existing, null, (byte[]?)channelPrefix),
                    channelPrefix.IsPattern ? RedisChannel.PatternMode.Pattern : RedisChannel.PatternMode.Literal);
            }

            return WithServices(new ChannelPrefixService(channelPrefix));
        }

        /// <summary>A copy of this context that sends through <paramref name="executor"/>.</summary>
        /// <param name="executor">The executor to send through.</param>
        internal RespContext WithExecutor(IRespExecutor? executor)
            => new(CommandMap, _keyPrefix, default, Database, ServerType, executor, _services);

        /// <summary>
        /// A copy of this context carrying <paramref name="services"/> <i>in addition to</i> whatever it
        /// already has.
        /// </summary>
        /// <param name="services">The service, or an <see cref="IServiceProvider"/>; <c>null</c> adds nothing.</param>
        /// <remarks>
        /// <para>
        /// Composing, never replacing. A context is built up in stages by callers who do not know each
        /// other - the multiplexer attaches a cache, a caller adds a probe - so a slot that assigned would
        /// make <c>.WithCache(x).WithServices(y)</c> quietly lose the cache, with nothing to see but cache
        /// misses much later. Re-binding still needs no code: the newest of a type wins by lookup order.
        /// </para>
        /// <para>
        /// Turning something off is <see cref="WithCache"/>/<see cref="WithScriptCache"/> with
        /// <c>null</c>, which shadows just that one rather than emptying the slot.
        /// </para>
        /// </remarks>
        public RespContext WithServices(object? services)
            => services is null
                ? this
                : new(CommandMap, _keyPrefix, default, Database, ServerType, Executor, ServiceLink.Add(_services, services));

        /// <summary>A copy of this context where <paramref name="serviceType"/> reads as absent.</summary>
        private RespContext WithoutService(Type serviceType)
            => _services is null ? this : WithServices(new ServiceVeto(serviceType));

        /// <summary>A copy of this context that consults <paramref name="cache"/>.</summary>
        /// <param name="cache">The cache to consult, or <c>null</c> for none.</param>
        /// <remarks>Sugar over <see cref="WithServices"/>; "a context with a cache" is just a context whose
        /// services include one.</remarks>
        internal RespContext WithCache(RespClientCache? cache)
            => cache is null ? WithoutService(typeof(RespClientCache)) : WithServices(cache);

        /// <summary>A copy of this context that does not consult the client-side cache.</summary>
        /// <remarks>
        /// <para>
        /// The opt-out, and the only direction a caller needs: attaching a cache is the multiplexer's job,
        /// because a cache is only sound while the connection it belongs to has negotiated
        /// <c>CLIENT TRACKING</c> for it. One that a caller minted and attached by hand would fill, expire
        /// on its own lifetime, and never be invalidated - silently, and for as long as the entries live.
        /// </para>
        /// <para>
        /// Shadows the cache for this context rather than emptying the service slot, so anything else
        /// attached alongside it survives. See <see cref="WithServices"/>.
        /// </para>
        /// </remarks>
        public RespContext WithoutCache() => WithoutService(typeof(RespClientCache));

        /// <summary>A copy of this context that renders each script only once.</summary>
        /// <param name="scripts">The registry to use, or <c>null</c> for none.</param>
        /// <remarks>Without one, a script's <c>SCRIPT LOAD</c> is rendered afresh on every call - correct,
        /// and wasteful for anything used more than once.</remarks>
        public RespContext WithScriptCache(RespScriptCache? scripts)
            => scripts is null ? WithoutService(typeof(RespScriptCache)) : WithServices(scripts);

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
        /// <b>The retry category is inferred from the command NAME, and the name is not the contract.</b>
        /// A name that parses - <c>XREAD</c>, <c>SORT</c>, <c>GETEX</c> - picks up the category this
        /// library assumes for its own typed use of that command, and the arguments that would change the
        /// answer are ones only the caller can see. <c>XREAD</c> is categorised read-only, so
        /// <c>Execute("XREAD", "BLOCK", 0, ...)</c> is treated as replayable <i>and cacheable</i>;
        /// <c>SORT</c> is read-only until a <c>STORE</c> argument makes it a write. The typed surface
        /// raises those cases explicitly because it can see the arguments; here, nobody can.
        /// </para>
        /// <para>
        /// <b>So say so</b> when an ad-hoc command is not what its name suggests:
        /// <c>flags.WithRetryCategory(CommandFlags.CommandRetryNever)</c> - or whichever rung fits. The
        /// caller's choice always wins over the inference. The default is kept as inference rather than
        /// "assume the worst" for compatibility with <c>IDatabase.Execute</c>, where the same reasoning
        /// has always applied.
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
            return this.SendAsync(ref frame, flags, RespHandlers.Result, default);
        }

        /// <summary>Render an ad-hoc command, marking each argument as a key or a value.</summary>
        private RespRequestFrame Render(string command, ReadOnlySpan<RedisKeyOrValue> args)
        {
            var handler = new RespRequestBuilder(0, args.Length, this, command);
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
            => WithServices(new MaxCacheAgeService(maxAge));

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
        /// The argument count is then only known at <see cref="RespRequestBuilder.Complete"/>, so the
        /// <c>*N</c> header is back-filled rather than written as a compile-time constant. Prefer the
        /// single-expression form for fixed-arity commands.
        /// <para>
        /// NOTE: the handler cannot be held by <c>using</c>, because a <c>using</c> variable cannot be passed
        /// by <c>ref</c> (CS1657). If the window between Compose and Execute can throw, use try/finally and
        /// call <see cref="RespRequestBuilder.Dispose"/>. This is the same constraint
        /// <c>DefaultInterpolatedStringHandler</c> lives under, and accepted for the same reason - see
        /// design doc section 6.5, where the identical precedent covers abandoning the rented buffer when
        /// an interpolation throws.
        /// </para>
        /// </remarks>
        public RespRequestBuilder Compose([InterpolatedStringHandlerArgument("")] ref RespRequestBuilder handler)
            => handler;

        /// <summary>
        /// As <see cref="Compose(ref RespRequestBuilder)"/>, but with the command supplied as a real
        /// argument rather than as the first hole:
        /// <code>
        /// var cmd = ctx.Compose(RedisCommand.SET, $"{key}{value}");
        /// </code>
        /// </summary>
        /// <remarks>
        /// <c>("", nameof(command))</c> passes the receiver <b>and</b> the command into the handler's
        /// constructor, which lets the command map be consulted before the buffer is rented.
        /// </remarks>
        internal RespRequestBuilder Compose(
            RedisCommand command,
            [InterpolatedStringHandlerArgument("", nameof(command))] ref RespRequestBuilder handler)
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
        internal RespRequestBuilder Compose(RedisCommand command, int argHint = 0)
            => new(0, argHint < 0 ? 0 : argHint, this, command);

        /// <summary>
        /// As the <c>RedisCommand</c> overload, taking a command <b>name</b>. The name is speculatively
        /// parsed to a known command so aliasing and disabling still apply; anything unrecognised is framed
        /// verbatim, as <c>IDatabase.Execute(string, ...)</c> does.
        /// </summary>
        /// <param name="command">The command name to issue.</param>
        /// <param name="handler">The interpolated arguments.</param>
        public RespRequestBuilder Compose(
            string command,
            [InterpolatedStringHandlerArgument("", nameof(command))] ref RespRequestBuilder handler)
            => handler;

        /// <summary>As the <c>RedisCommand</c> overload, taking a command <b>name</b>.</summary>
        /// <param name="command">The command name to issue.</param>
        /// <param name="handler">The interpolated arguments.</param>
        public RespRequestFrame Render(
            string command,
            [InterpolatedStringHandlerArgument("", nameof(command))] ref RespRequestBuilder handler)
            => Render(ref handler);

        /// <summary>
        /// As <see cref="Render(ref RespRequestBuilder)"/>, with the command as a real argument.
        /// </summary>
        internal RespRequestFrame Render(
            RedisCommand command,
            [InterpolatedStringHandlerArgument("", nameof(command))] ref RespRequestBuilder handler)
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
        public RespRequestFrame Render([InterpolatedStringHandlerArgument("")] ref RespRequestBuilder handler)
        {
            return handler.Complete();
        }
    }
}
