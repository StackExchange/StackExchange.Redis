using System;
using System.Runtime.CompilerServices;
using System.Threading;

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
    internal readonly struct RespContext
    {
        public RespContext(
            CommandMap? commandMap = null,
            RedisKey keyPrefix = default,
            RedisChannel channelPrefix = default,
            int database = 0,
            ServerType serverType = ServerType.Standalone,
            CancellationToken cancellationToken = default)
        {
            _commandMap = commandMap;
            _keyPrefix = keyPrefix; // normalise to bytes ONCE; the conversion can allocate for a string-backed key
            ChannelPrefix = channelPrefix;
            Database = database;
            ServerType = serverType;
            CancellationToken = cancellationToken;
        }

        private readonly CommandMap? _commandMap;

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

        public RedisChannel ChannelPrefix { get; }

        public int Database { get; }

        public ServerType ServerType { get; }

        public CancellationToken CancellationToken { get; }

        public RespContext WithCancellationToken(CancellationToken cancellationToken)
            => new(CommandMap, KeyPrefix, ChannelPrefix, Database, ServerType, cancellationToken);

        public RespContext WithDatabase(int database)
            => new(CommandMap, KeyPrefix, ChannelPrefix, database, ServerType, CancellationToken);

        public RespContext WithServerType(ServerType serverType)
            => new(CommandMap, KeyPrefix, ChannelPrefix, Database, serverType, CancellationToken);

        /// <summary>
        /// Returns a context whose keys are prefixed. This is what replaces wrapping the database in a
        /// <c>KeyPrefixedDatabase</c> decorator: the prefix is state on a value type, not a new object graph.
        /// Nested calls compose, matching the decorator's behaviour.
        /// </summary>
        public RespContext WithKeyPrefix(RedisKey keyPrefix)
            => new(
                CommandMap,
                _keyPrefix is null ? keyPrefix : RedisKey.WithPrefix(_keyPrefix, keyPrefix),
                ChannelPrefix,
                Database,
                ServerType,
                CancellationToken);

        public RespContext WithChannelPrefix(RedisChannel channelPrefix)
            => new(CommandMap, KeyPrefix, channelPrefix, Database, ServerType, CancellationToken);

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
        /// using var frame = ctx.Execute(ref cmd);
        /// </code>
        /// </summary>
        /// <remarks>
        /// The argument count is then only known at <see cref="RespCommandHandler.Complete"/>, so the
        /// <c>*N</c> header is back-filled rather than written as a compile-time constant. Prefer the
        /// single-expression form for fixed-arity commands.
        /// <para>
        /// NOTE: the handler cannot be held by <c>using</c>, because a <c>using</c> variable cannot be passed
        /// by <c>ref</c> (CS1657). If the window between Compose and Execute can throw, use try/finally and
        /// call <see cref="RespCommandHandler.Dispose"/>.
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
        public RespCommandHandler Compose(
            RedisCommand command,
            [InterpolatedStringHandlerArgument("", nameof(command))] ref RespCommandHandler handler)
            => handler;

        /// <summary>
        /// Initialize a builder with no interpolated part at all, for a fully dynamic argument list:
        /// <code>
        /// var cmd = ctx.Compose(RedisCommand.DEL, keys.Length);
        /// foreach (var key in keys) cmd.AppendFormatted(key);
        /// using var frame = ctx.Execute(ref cmd);
        /// </code>
        /// </summary>
        /// <param name="command">The command to issue.</param>
        /// <param name="argHint">Expected number of arguments, used only to size the initial rent.</param>
        public RespCommandHandler Compose(RedisCommand command, int argHint = 0)
            => new(0, argHint < 0 ? 0 : argHint, this, command);

        /// <summary>
        /// As <see cref="Execute(ref RespCommandHandler)"/>, with the command as a real argument.
        /// </summary>
        public RespFrame Execute(
            RedisCommand command,
            [InterpolatedStringHandlerArgument("", nameof(command))] ref RespCommandHandler handler)
            => Execute(ref handler);

        public RespFrame Execute([InterpolatedStringHandlerArgument("")] ref RespCommandHandler handler)
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
