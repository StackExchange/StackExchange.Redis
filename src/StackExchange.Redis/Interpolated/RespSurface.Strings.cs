using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using RESPite;
using RESPite.Messages;

namespace StackExchange.Redis.Interpolated
{
    /// <summary>
    /// EXPERIMENTAL SPIKE. The string-command group: <c>target.Strings.Set(...)</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A plain wrapper over one <see cref="RespContext"/> field - deliberately NOT a reinterpret-cast of a
    /// layout-compatible struct. Holding exactly one field of that type makes the layout identical <i>by
    /// construction</i>, so the wrapper IS the pun, enforced by the compiler and with no <c>Unsafe</c>. A
    /// by-value pun would copy the same bytes anyway; only a <c>ref</c> pun avoids the copy, and that
    /// requires a stable address, which drags <c>ref readonly</c> and its lifetime rules into every caller
    /// to save a few register moves ahead of a network round trip.
    /// </para>
    /// <para>
    /// Not a <c>ref struct</c>, for the same reason <see cref="RespContext"/> is not: these have to survive
    /// an <c>await</c>.
    /// </para>
    /// </remarks>
    [Experimental(Experiments.InterpolatedWriter, UrlFormat = Experiments.UrlFormat)]
    public readonly struct RespStrings
    {
        private readonly RespContext _context;

        /// <summary>Group the string commands of a context.</summary>
        /// <param name="context">The context to send through.</param>
        public RespStrings(in RespContext context) => _context = context;

        /// <summary>The underlying context.</summary>
        public RespContext Context => _context;
    }

    public static partial class RespSurface
    {
        extension(IRespKeyspaceTarget target)
        {
            /// <summary>The string commands.</summary>
            public RespStrings Strings => new(target.Context);
        }

        extension(in RespContext context)
        {
            /// <summary>The string commands.</summary>
            public RespStrings Strings => new(context);
        }

        // The group ACCESSORS above have to be extension blocks - an extension property has no other
        // spelling. The COMMANDS below are deliberately ordinary `this` extension methods, and that is
        // not nostalgia: it is what makes retiring one cheap later. Decommissioning an obsolete command
        // is then a matter of deleting the `this` - new call sites bind to whatever replaced it, while
        // already-compiled callers keep working, because the static method they were compiled against is
        // still there, same name, same signature, same assembly. No MissingMethodException, no major
        // version. An extension block member cannot be retired that gently. See AGENTS.md, "Backwards
        // compatibility is paramount".
        //
        // `in` because RespStrings is a readonly struct: no defensive copy, and nothing to copy on the
        // way to a network round trip.

        // The retry category comes from CommandFlagsExtensions.WithDefaultCategory - the same per-command
        // table the MessageWriter path uses - rather than a constant named at each call site. Two writers
        // agreeing on the bytes and disagreeing on whether a command is safe to replay is the kind of
        // divergence nothing would catch; the table is the single source of truth, and a command whose
        // ARGUMENTS change the answer (GETEX with a TTL, SET under NX) raises it explicitly and says why.
        // WithRetryCategory stays public for surfaces outside this assembly, which cannot see the table.

        /// <summary>
        /// Run an arbitrary command and return the raw reply - the escape hatch, reachable from anything
        /// that can produce a context.
        /// </summary>
        /// <param name="target">The database, or anything else carrying a context.</param>
        /// <param name="command">The command name.</param>
        /// <param name="args">The arguments, each already known to be a key or a value.</param>
        /// <param name="flags">The command's flags.</param>
        /// <remarks>
        /// One extension method, and <c>RespDatabase</c>, <c>IDatabase</c> and anything else implementing
        /// <see cref="IRespTarget"/> all gain it without being touched - which is section 9.4's argument
        /// working rather than being asserted. See <see cref="RespContext.ExecuteAsync"/> for why the
        /// argument type matters.
        /// </remarks>
        public static ValueTask<RespResult> ExecuteAsync(
            this IRespTarget target,
            string command,
            ReadOnlyMemory<RedisKeyOrValue> args,
            CommandFlags flags = CommandFlags.None)
            => target.Context.ExecuteAsync(command, args, flags);

        /// <summary>GET.</summary>
        /// <param name="strings">The string command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<RedisValue> Get(this in RespStrings strings, RedisKey key, CommandFlags flags = CommandFlags.None)
            => strings.Context.SendAsync<RedisValue>(
                $"{RedisCommand.GET}{key}", flags.WithDefaultCategory(RedisCommand.GET));

        /// <summary>MGET.</summary>
        /// <param name="strings">The string command group.</param>
        /// <param name="keys">The keys to read.</param>
        /// <param name="flags">Command flags.</param>
        /// <remarks>
        /// <para>
        /// The variadic form, in one expression: <c>{keys}</c> is a hole like any other, and each key in it
        /// is prefixed, marked for invalidation and folded into the cluster slot exactly as a single key is.
        /// Without that hole this would be <c>Compose</c>, a loop and a <c>try</c>/<c>finally</c>.
        /// </para>
        /// <para>
        /// No keys means no command: an arity-zero <c>MGET</c> is a server error, and "the values of no
        /// keys" is an empty array without asking anyone. The send is skipped, so this completes
        /// synchronously and allocates nothing.
        /// </para>
        /// <para>
        /// <b>A pooled lease of windows, not an array of values</b>, and it must be disposed. The reply is
        /// a block the caller almost always walks once, so an array means a per-call allocation nothing
        /// can reclaim; a lease can be given back. The elements are <see cref="RespValue"/> rather than
        /// <see cref="RedisValue"/>, which is what makes them free: a value is a window into the one reply
        /// buffer the lease holds, where a <c>RedisValue</c> has no lifetime and so has to own its bytes -
        /// an allocation each, for anything that is neither small nor a canonical number.
        /// </para>
        /// <para>
        /// Each element reads with <c>AsInt64</c>, <c>AsRedisValue</c>, <c>(string?)</c> and the rest, and
        /// stays valid until the lease is disposed. The array shape the old surface still needs lives on
        /// the internal <c>GetArray</c> sibling, which pays one <c>AsRedisValue</c> per element.
        /// </para>
        /// </remarks>
        public static ValueTask<ReadOnlyLease<RespValue>> Get(this in RespStrings strings, ReadOnlySpan<RedisKey> keys, CommandFlags flags = CommandFlags.None)
            => keys.IsEmpty
                ? new ValueTask<ReadOnlyLease<RespValue>>(ReadOnlyLease<RespValue>.Empty)
                : strings.Context.SendAsync(
                    $"{RedisCommand.MGET}{keys}", flags.WithDefaultCategory(RedisCommand.MGET), RespHandlers.ValueWindowHandler.Instance);

        /// <summary>MGET, as an array, for the old <c>IDatabase</c> surface.</summary>
        /// <param name="strings">The string command group.</param>
        /// <param name="keys">The keys to read.</param>
        /// <param name="flags">Command flags.</param>
        /// <remarks>
        /// <para>
        /// <b>Internal, and deliberately a sibling rather than a conversion.</b> <c>IDatabase.StringGet</c>
        /// promises an array the caller owns outright, so bridging through <c>Get</c> would rent a
        /// pooled buffer only to copy out of it and hand it straight back - strictly worse than allocating
        /// the array in the first place. Two handlers over one command costs one duplicated interpolated
        /// line and no knowledge; the command is still written once anywhere it matters.
        /// </para>
        /// <para>
        /// <b>Permanent, not scaffolding.</b> <c>IDatabase</c> is not going anywhere - compatibility
        /// outranks tidiness here - so this is how <c>StringGet(RedisKey[])</c> is served from the new
        /// core, for as long as that signature exists. Internal because the array is the <i>old</i>
        /// spelling: new code should reach for the lease, and nothing outside this assembly should be able
        /// to choose otherwise.
        /// </para>
        /// </remarks>
        internal static ValueTask<RedisValue[]> GetArray(this in RespStrings strings, ReadOnlySpan<RedisKey> keys, CommandFlags flags = CommandFlags.None)
            => keys.IsEmpty
                ? new ValueTask<RedisValue[]>(Array.Empty<RedisValue>())
                : strings.Context.SendAsync(
                    $"{RedisCommand.MGET}{keys}", flags.WithDefaultCategory(RedisCommand.MGET), RespHandlers.Values);

        /// <summary>GET, retaining the payload as a <see cref="ReadOnlyLease{T}"/> rather than a value.</summary>
        /// <param name="strings">The string command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="flags">Command flags.</param>
        /// <remarks>
        /// <para>
        /// Same command, different result shape - which is why it is a separate method rather than an
        /// overload: the two differ only in return type, and C# does not overload on that. The lease must
        /// be disposed.
        /// </para>
        /// <para>
        /// <b>Read-only, so it can share.</b> This is the whole point of the lease over a value: where the
        /// reply is retained contiguously, the lease points at it rather than copying. A writable lease
        /// could not - a buffer the caller may scribble on must not alias memory anything else can read -
        /// so the mutable <see cref="Lease{T}"/> always copies. The old surface's signatures say
        /// <see cref="Lease{T}"/> and cannot change, which is what <see cref="GetWritableLease(in RespStrings, RedisKey, CommandFlags)"/> is for.
        /// </para>
        /// </remarks>
        public static ValueTask<ReadOnlyLease<byte>?> GetLease(this in RespStrings strings, RedisKey key, CommandFlags flags = CommandFlags.None)
            => strings.Context.SendAsync<ReadOnlyLease<byte>?>(
                $"{RedisCommand.GET}{key}", flags.WithDefaultCategory(RedisCommand.GET));

        /// <inheritdoc cref="GetLease(in RespStrings, RedisKey, CommandFlags)"/>
        /// <param name="strings">The string command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="flags">Command flags.</param>
        /// <remarks>
        /// The writable-lease sibling, for <c>IDatabase.StringGetLease</c>. Internal for the reason
        /// <see cref="GetArray(in RespStrings, ReadOnlySpan{RedisKey}, CommandFlags)"/> is: the mutable lease is the <i>old</i> spelling, it copies where the
        /// read-only one need not, and nothing outside this assembly should be able to choose it.
        /// </remarks>
        internal static ValueTask<Lease<byte>?> GetWritableLease(this in RespStrings strings, RedisKey key, CommandFlags flags = CommandFlags.None)
            => strings.Context.SendAsync<Lease<byte>?>(
                $"{RedisCommand.GET}{key}", flags.WithDefaultCategory(RedisCommand.GET));

        /// <summary>GETRANGE.</summary>
        /// <param name="strings">The string command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="start">The inclusive start offset; negative counts back from the end.</param>
        /// <param name="end">The inclusive end offset; negative counts back from the end.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<RedisValue> GetRange(this in RespStrings strings, RedisKey key, long start, long end, CommandFlags flags = CommandFlags.None)
            => strings.Context.SendAsync<RedisValue>(
                $"{RedisCommand.GETRANGE}{key}{start}{end}", flags.WithDefaultCategory(RedisCommand.GETRANGE));

        /// <summary>GETDEL.</summary>
        /// <param name="strings">The string command group.</param>
        /// <param name="key">The key to read and remove.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<RedisValue> GetDelete(this in RespStrings strings, RedisKey key, CommandFlags flags = CommandFlags.None)
            => strings.Context.SendAsync<RedisValue>(
                $"{RedisCommand.GETDEL}{key}", flags.WithDefaultCategory(RedisCommand.GETDEL));

        /// <summary>GETEX: read the value, and set, keep or clear the expiration in the same call.</summary>
        /// <param name="strings">The string command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="expiry">
        /// The expiration to apply; <see cref="Expiration.Default"/> leaves the TTL untouched, and
        /// <see cref="Expiration.Persist"/> clears it.
        /// </param>
        /// <param name="flags">Command flags.</param>
        /// <remarks>
        /// <para>
        /// <b>One method where the old surface has three.</b> <c>StringGetSetExpiry</c> exists as a
        /// <c>TimeSpan?</c> overload and a <c>DateTime</c> one, and neither can say PERSIST -
        /// <c>StringPersist</c> is a separate command. <see cref="Expiration"/> already spells all of
        /// those, so taking it collapses the set without losing a single case.
        /// </para>
        /// <para>
        /// The retry category depends on the argument: a bare <c>GETEX</c> is the pure read the table says
        /// it is, but any of EX/PX/EXAT/PXAT/PERSIST mutates the TTL and makes it a write. ENX has no
        /// spelling here at all, and <see cref="Expiration.GetTokenCount"/> says so rather than letting it
        /// render into a command the server will reject.
        /// </para>
        /// </remarks>
        public static ValueTask<RedisValue> GetSetExpiry(this in RespStrings strings, RedisKey key, Expiration expiry, CommandFlags flags = CommandFlags.None)
        {
            var mutatesTtl = expiry.GetTokenCount(allowEnx: false) != 0;
            if (mutatesTtl) flags = flags.WithRetryCategory(CommandFlags.CommandRetryWriteLastWins);

            return strings.Context.SendAsync<RedisValue>(
                $"{RedisCommand.GETEX}{key}{expiry}", flags.WithDefaultCategory(RedisCommand.GETEX));
        }

        /// <summary>STRLEN.</summary>
        /// <param name="strings">The string command group.</param>
        /// <param name="key">The key to measure.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<long> Length(this in RespStrings strings, RedisKey key, CommandFlags flags = CommandFlags.None)
            => strings.Context.SendAsync<long>(
                $"{RedisCommand.STRLEN}{key}", flags.WithDefaultCategory(RedisCommand.STRLEN));

        /// <summary>APPEND; the reply is the new length.</summary>
        /// <param name="strings">The string command group.</param>
        /// <param name="key">The key to append to.</param>
        /// <param name="value">The value to append.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<long> Append(this in RespStrings strings, RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
            => strings.Context.SendAsync<long>(
                $"{RedisCommand.APPEND}{key}{value}", flags.WithDefaultCategory(RedisCommand.APPEND));

        /// <summary>SETRANGE; the reply is the new length.</summary>
        /// <param name="strings">The string command group.</param>
        /// <param name="key">The key to write into.</param>
        /// <param name="offset">The byte offset to write at; the value is zero-padded up to it.</param>
        /// <param name="value">The value to write.</param>
        /// <param name="flags">Command flags.</param>
        /// <remarks>
        /// <c>long</c>, not <c>RedisValue</c>. <c>SETRANGE</c> replies with an integer and always has; the
        /// old surface returns <c>RedisValue</c>, which makes every caller ask a second question of a reply
        /// that only ever answers one way. The adapter converts, so nothing observable changes for the old
        /// spelling.
        /// </remarks>
        public static ValueTask<long> SetRange(this in RespStrings strings, RedisKey key, long offset, RedisValue value, CommandFlags flags = CommandFlags.None)
            => strings.Context.SendAsync<long>(
                $"{RedisCommand.SETRANGE}{key}{offset}{value}", flags.WithDefaultCategory(RedisCommand.SETRANGE));

        /// <summary>DIGEST: the server's hash of the value, as a condition a later write can be gated on.</summary>
        /// <param name="strings">The string command group.</param>
        /// <param name="key">The key to digest.</param>
        /// <param name="flags">Command flags.</param>
        /// <remarks>
        /// <see langword="null"/> when the key does not exist. The result is directly usable as the
        /// <c>when</c> of a later <see cref="Set(in RespStrings, RedisKey, RedisValue, Expiration, ValueCondition, CommandFlags)"/>,
        /// which is the whole point of returning a <see cref="ValueCondition"/> rather than bytes.
        /// </remarks>
        public static ValueTask<ValueCondition?> Digest(this in RespStrings strings, RedisKey key, CommandFlags flags = CommandFlags.None)
            => strings.Context.SendAsync<ValueCondition?>(
                $"{RedisCommand.DIGEST}{key}", flags.WithDefaultCategory(RedisCommand.DIGEST));

        /// <summary>SET, in full: expiration and value condition included.</summary>
        /// <param name="strings">The string command group.</param>
        /// <param name="key">The key to write.</param>
        /// <param name="value">The value to write.</param>
        /// <param name="expiry">When the key should expire; default for no expiration.</param>
        /// <param name="when">The condition the write is subject to; default to write unconditionally.</param>
        /// <param name="flags">Command flags.</param>
        /// <remarks>
        /// <para>
        /// Deliberately the <b>most complicated</b> command in the spike, because it is the one that tests
        /// the design rather than demonstrating it: between them <paramref name="expiry"/> and
        /// <paramref name="when"/> render anywhere from zero to five extra arguments, so the command's
        /// arity is not known until run time.
        /// </para>
        /// <para>
        /// It is still one straight line of writing. The legacy builder
        /// (<c>RedisDatabase.GetStringSetMessage</c>) is a ~17-branch decision tree, and most of those
        /// branches are not about Redis at all - they pick between fixed-arity <c>Message.Create</c>
        /// overloads, one branch per token count, which is a cost the interpolated form simply does not
        /// have.
        /// </para>
        /// <para>
        /// <b>Condition before expiration</b>, which is the documented grammar:
        /// <c>SET key value [NX|XX|IFEQ cmp] [GET] [EX s|PX ms|EXAT|PXAT|KEEPTTL]</c>. Redis itself parses
        /// the tail as an order-insensitive loop - which is how the legacy builder gets away with emitting
        /// <c>EX n XX</c> - but other RESP servers need not be as forgiving, and matching the documentation
        /// costs nothing.
        /// </para>
        /// <para>
        /// <b>It emits the canonical <c>SET</c> and nothing else</b>, where the legacy builder also reaches
        /// for <c>SETNX</c>, <c>SETEX</c> and <c>PSETEX</c>. <c>SETEX</c>/<c>PSETEX</c> are pure arity
        /// relics - identical semantics and reply to <c>SET ... EX n</c>. <c>SETNX</c> is <b>not</b>: it
        /// replies <c>:1</c>/<c>:0</c> where <c>SET ... NX</c> replies <c>+OK</c>/nil, so collapsing it here
        /// is a real divergence from the old surface, taken deliberately - <c>SET ... NX</c> has been
        /// available since 2.6.12 and one reply shape beats two.
        /// </para>
        /// <para>
        /// <b>One expression, including the optional arguments.</b> Compose/Complete is not needed here -
        /// that pairing exists for a fragment whose <i>presence</i> is a branch in the caller's logic, and
        /// nothing here branches. The result stays <c>ValueTask&lt;bool&gt;</c> rather than the result-less
        /// <c>SendAsync</c>, because under NX/XX/IFEQ the boolean is real information: a nil reply means
        /// the write did not happen, which is not an error.
        /// </para>
        /// <para>
        /// The retry category comes from the condition: a conditional write is checked, an unconditional
        /// one is last-wins. <see cref="ValueCondition.RetryCategory"/> returns
        /// <see cref="CommandFlags.None"/> for "no opinion", and <c>WithRetryCategory</c> is first-wins, so
        /// a caller who names a category still keeps it.
        /// </para>
        /// <para>
        /// <b>A null value deletes the key</b>, as it always has on this library's surface. There is no
        /// <c>SET</c> that stores "no value" - the nearest thing the protocol offers is an empty string,
        /// which is a <i>different</i> value, and writing that instead would turn "remove this" into
        /// "store nothing here" without saying so. The condition and expiration have no meaning for a
        /// delete and are dropped, which is also what the old builder does.
        /// </para>
        /// </remarks>
        public static ValueTask<bool> Set(
            this in RespStrings strings,
            RedisKey key,
            RedisValue value,
            Expiration expiry = default,
            ValueCondition when = default,
            CommandFlags flags = CommandFlags.None)
            => value.IsNull
                ? Delete(in strings, key, when: default, flags)
                : strings.Context.SendAsync<bool>(
                    $"{RedisCommand.SET}{key}{value}{when}{expiry}",
                    flags.WithRetryCategory(when.RetryCategory)
                         .WithDefaultCategory(RedisCommand.SET));

        /// <summary>MSET/MSETNX/MSETEX: set several keys in one command.</summary>
        /// <param name="strings">The string command group.</param>
        /// <param name="values">The key/value pairs to write.</param>
        /// <param name="expiry">When the keys should expire; default for no expiration.</param>
        /// <param name="when">The condition the write is subject to; default to write unconditionally.</param>
        /// <param name="flags">Command flags.</param>
        /// <remarks>
        /// <para>
        /// <b>Three commands behind one method, and unlike SET's arity relics these are not
        /// interchangeable.</b> <c>MSETEX</c> alone can carry an expiration or a condition, but it is a
        /// recent addition; <c>MSET</c> and <c>MSETNX</c> have been there since 1.0.1. So the choice is
        /// made on what the caller actually asked for, and the widely-available command is used whenever
        /// it can express the request - which is a server-version decision, not an arity one, and is why
        /// this branch survives where SET's did not.
        /// </para>
        /// <para>
        /// A value condition beyond NX/XX has no multi-key spelling at all, and says so here rather than
        /// rendering a command the server will reject.
        /// </para>
        /// <para>
        /// No pairs means no command, as with <see cref="Get(in RespStrings, ReadOnlySpan{RedisKey}, CommandFlags)"/>:
        /// writing nothing succeeded.
        /// </para>
        /// </remarks>
        public static ValueTask<bool> Set(
            this in RespStrings strings,
            ReadOnlySpan<KeyValuePair<RedisKey, RedisValue>> values,
            Expiration expiry = default,
            ValueCondition when = default,
            CommandFlags flags = CommandFlags.None)
        {
            if (values.IsEmpty) return new ValueTask<bool>(true);

            var command = when.Kind switch
            {
                ValueCondition.ConditionKind.Always when expiry.IsNone => RedisCommand.MSET,

                // "keep the TTL" and "the key must not exist" cannot disagree: there is no TTL to keep
                ValueCondition.ConditionKind.NotExists when expiry.IsNoneOrKeepTtl => RedisCommand.MSETNX,

                ValueCondition.ConditionKind.Always
                    or ValueCondition.ConditionKind.Exists
                    or ValueCondition.ConditionKind.NotExists => RedisCommand.MSETEX,

                _ => ThrowUnsupportedCondition<RedisCommand>(when, nameof(Set)),
            };

            flags = flags.WithRetryCategory(when.RetryCategory).WithDefaultCategory(command);

            // MSET/MSETNX take the pairs and nothing else; MSETEX prefixes a count and accepts the tail
            return command == RedisCommand.MSETEX
                ? strings.Context.SendAsync<bool>($"{command}{values.Length}{values}{expiry}{when}", flags)
                : strings.Context.SendAsync<bool>($"{command}{values}", flags);
        }

        /// <summary>SET ... GET: write the value, and reply with the one it replaced.</summary>
        /// <param name="strings">The string command group.</param>
        /// <param name="key">The key to write.</param>
        /// <param name="value">The value to write.</param>
        /// <param name="expiry">When the key should expire; default for no expiration.</param>
        /// <param name="when">The condition the write is subject to; default to write unconditionally.</param>
        /// <param name="flags">Command flags.</param>
        /// <remarks>
        /// <para>
        /// The canonical form, and <c>GETSET</c> is not emitted at all - it has been deprecated in favour
        /// of <c>SET ... GET</c> since 6.2, and unlike <c>GETSET</c> this one composes with NX/XX and with
        /// an expiration.
        /// </para>
        /// <para>
        /// <b>Operand order is the documented grammar</b>, as in
        /// <see cref="Set(in RespStrings, RedisKey, RedisValue, Expiration, ValueCondition, CommandFlags)"/>:
        /// the condition, then <c>GET</c>, then the expiration. The old builder emits <c>EX n XX GET</c>,
        /// which Redis parses and another RESP server need not.
        /// </para>
        /// <para>
        /// A nil reply is ambiguous by nature - the key was absent, or the condition refused the write -
        /// and that ambiguity is the command's, not ours. A caller who needs to tell them apart wants
        /// <see cref="Set(in RespStrings, RedisKey, RedisValue, Expiration, ValueCondition, CommandFlags)"/>,
        /// whose boolean answers exactly that question.
        /// </para>
        /// </remarks>
        public static ValueTask<RedisValue> SetAndGet(
            this in RespStrings strings,
            RedisKey key,
            RedisValue value,
            Expiration expiry = default,
            ValueCondition when = default,
            CommandFlags flags = CommandFlags.None)
            => value.IsNull
                ? GetDelete(in strings, key, flags) // as Set: a null value removes the key, and GETDEL is the read-it-back form
                : strings.Context.SendAsync<RedisValue>(
                    $"{RedisCommand.SET}{key}{value}{when}{RespLiterals.Get}{expiry}",
                    flags.WithRetryCategory(when.RetryCategory)
                         .WithDefaultCategory(RedisCommand.SET));

        /// <summary>
        /// <c>SET ... GET</c> where the server has it, <c>GETSET</c> where it does not: the old
        /// <c>StringGetSet</c> shape, and nothing more.
        /// </summary>
        /// <param name="strings">The string command group.</param>
        /// <param name="key">The key to write.</param>
        /// <param name="value">The value to write.</param>
        /// <param name="flags">Command flags.</param>
        /// <remarks>
        /// <para>
        /// Internal, and deliberately not part of the group's own surface: the modern spelling is
        /// <see cref="SetAndGet(in RespStrings, RedisKey, RedisValue, Expiration, ValueCondition, CommandFlags)"/>,
        /// which composes with a condition and an expiration as <c>GETSET</c> never could. This exists so
        /// that a caller of the <b>old</b> method keeps working against a server older than 6.2, where
        /// <c>SET ... GET</c> is a syntax error.
        /// </para>
        /// <para>
        /// A null value throws, as the old method always has - it builds a key/value message, and those
        /// assert. <c>SetAndGet</c> instead reads a null as a delete, matching <c>Set</c>; inheriting that
        /// here would turn a call that used to fail loudly into one that silently removes the key.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentException">If <paramref name="value"/> is null.</exception>
        internal static ValueTask<RedisValue> GetSet(
            this in RespStrings strings,
            RedisKey key,
            RedisValue value,
            CommandFlags flags = CommandFlags.None)
        {
            value.AssertNotNull();

            var context = strings.Context;
            if (context.CommandMap.IsAvailable(RedisCommand.SET)
                && context.TryGetFeatures(RedisCommand.SET, in key, flags, out var features)
                && features.SetAndGet)
            {
                return context.SendAsync<RedisValue>(
                    $"{RedisCommand.SET}{key}{value}{RespLiterals.Get}",
                    flags.WithDefaultCategory(RedisCommand.SET));
            }

            // not known to be 6.2+, so the deprecated spelling, which every server understands. "Not sure"
            // has to mean the old one: the new one fails outright where it is missing.
            return context.SendAsync<RedisValue>(
                $"{RedisCommand.GETSET}{key}{value}",
                flags.WithDefaultCategory(RedisCommand.GETSET));
        }

        /// <summary>DEL/DELEX: remove a key, optionally only if it still holds what you think it does.</summary>
        /// <param name="strings">The string command group.</param>
        /// <param name="key">The key to remove.</param>
        /// <param name="when">The condition the delete is subject to; default to delete unconditionally.</param>
        /// <param name="flags">Command flags.</param>
        /// <remarks>
        /// <para>
        /// <see cref="ValueCondition.Exists"/> is the same request as no condition - <c>DEL</c> already
        /// means "if it is there" - so both render <c>DEL</c>. A value or digest test needs <c>DELEX</c>,
        /// which is the whole reason this takes a condition at all.
        /// </para>
        /// <para>
        /// <see cref="ValueCondition.NotExists"/> has no meaning here and is rejected: "delete it if it is
        /// absent" is not a request the server can be asked, and quietly treating it as
        /// <see cref="ValueCondition.Always"/> would delete the key the caller was protecting.
        /// </para>
        /// </remarks>
        public static ValueTask<bool> Delete(
            this in RespStrings strings,
            RedisKey key,
            ValueCondition when = default,
            CommandFlags flags = CommandFlags.None)
        {
            switch (when.Kind)
            {
                case ValueCondition.ConditionKind.Always:
                case ValueCondition.ConditionKind.Exists:
                    return strings.Context.SendAsync<bool>(
                        $"{RedisCommand.DEL}{key}", flags.WithDefaultCategory(RedisCommand.DEL));

                case ValueCondition.ConditionKind.ValueEquals:
                case ValueCondition.ConditionKind.ValueNotEquals:
                case ValueCondition.ConditionKind.DigestEquals:
                case ValueCondition.ConditionKind.DigestNotEquals:
                    return strings.Context.SendAsync<bool>(
                        $"{RedisCommand.DELEX}{key}{when}",
                        flags.WithRetryCategory(when.RetryCategory).WithDefaultCategory(RedisCommand.DELEX));

                default:
                    return ThrowUnsupportedCondition<ValueTask<bool>>(when, nameof(Delete));
            }
        }

        /// <summary>INCRBY, and INCRBYFLOAT for the floating-point twin.</summary>
        /// <param name="strings">The string command group.</param>
        /// <param name="key">The key to increment.</param>
        /// <param name="value">The amount to add.</param>
        /// <param name="flags">Command flags.</param>
        /// <remarks>
        /// <para>
        /// <b>There is deliberately no Decrement.</b> <c>DECRBY key n</c> and <c>INCRBY key -n</c> are the
        /// same request with the same reply, and the old surface already implements one as the other -
        /// <c>StringDecrement</c> is a negation and a call to <c>StringIncrement</c>. Keeping the second
        /// spelling here would buy a method whose only content is a minus sign.
        /// </para>
        /// <para>
        /// <c>INCR</c> and <c>DECR</c> go the same way, for the reason <c>SETEX</c> did: they are
        /// <c>INCRBY key 1</c> with the argument removed, which saves four bytes on the wire and costs a
        /// branch on every call.
        /// </para>
        /// </remarks>
        public static ValueTask<long> Increment(this in RespStrings strings, RedisKey key, long value = 1, CommandFlags flags = CommandFlags.None)
            => strings.Context.SendAsync<long>(
                $"{RedisCommand.INCRBY}{key}{value}", flags.WithDefaultCategory(RedisCommand.INCRBY));

        /// <inheritdoc cref="Increment(in RespStrings, RedisKey, long, CommandFlags)"/>
        /// <param name="strings">The string command group.</param>
        /// <param name="key">The key to increment.</param>
        /// <param name="value">The amount to add.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<double> Increment(this in RespStrings strings, RedisKey key, double value, CommandFlags flags = CommandFlags.None)
            => strings.Context.SendAsync<double>(
                $"{RedisCommand.INCRBYFLOAT}{key}{value}", flags.WithDefaultCategory(RedisCommand.INCRBYFLOAT));

        /// <summary>INCREX: increment with an expiration, and optionally with bounds.</summary>
        /// <param name="strings">The string command group.</param>
        /// <param name="key">The key to increment.</param>
        /// <param name="value">The amount to add.</param>
        /// <param name="expiry">When the key should expire; <c>ENX</c> applies it only to a new key.</param>
        /// <param name="lowerBound">The lowest value the result may take, if any.</param>
        /// <param name="upperBound">The highest value the result may take, if any.</param>
        /// <param name="options">Whether a bound clamps the result or rejects the increment.</param>
        /// <param name="flags">Command flags.</param>
        /// <remarks>
        /// <para>
        /// A separate command rather than an optional argument on
        /// <see cref="Increment(in RespStrings, RedisKey, long, CommandFlags)"/>: the reply shape differs -
        /// <c>INCREX</c> answers with the new value <i>and</i> the increment that was actually applied,
        /// which under a bound is not the one you asked for.
        /// </para>
        /// <para>
        /// <c>KEEPTTL</c> and <c>PERSIST</c> have no spelling here, and a bare <c>ENX</c> without an
        /// expiration is not a request; all three are rejected at the call site rather than rendered into
        /// a command the server refuses.
        /// </para>
        /// </remarks>
        public static ValueTask<StringIncrementResult<long>> Increment(
            this in RespStrings strings,
            RedisKey key,
            long value,
            Expiration expiry,
            long? lowerBound = null,
            long? upperBound = null,
            IncrementOptions options = IncrementOptions.None,
            CommandFlags flags = CommandFlags.None)
        {
            ValidateIncrementExpiry(expiry);

            var cmd = strings.Context.Compose(RedisCommand.INCREX, argHint: 9);
            try
            {
                cmd.Append($"{key}{RespLiterals.ByInt}{value}");
                if (lowerBound.HasValue) cmd.Append($"{RespLiterals.LBound}{lowerBound.GetValueOrDefault()}");
                if (upperBound.HasValue) cmd.Append($"{RespLiterals.UBound}{upperBound.GetValueOrDefault()}");
                cmd.Append($"{AsFragment(options)}{expiry}");
            }
            catch
            {
                cmd.Dispose();
                throw;
            }

            var frame = cmd.Complete();
            return strings.Context.SendAsync(ref frame, flags.WithDefaultCategory(RedisCommand.INCREX), RespHandlers.Inbuilt<StringIncrementResult<long>>.Require());
        }

        /// <inheritdoc cref="Increment(in RespStrings, RedisKey, long, Expiration, long?, long?, IncrementOptions, CommandFlags)"/>
        /// <param name="strings">The string command group.</param>
        /// <param name="key">The key to increment.</param>
        /// <param name="value">The amount to add.</param>
        /// <param name="expiry">When the key should expire; <c>ENX</c> applies it only to a new key.</param>
        /// <param name="lowerBound">The lowest value the result may take, if any.</param>
        /// <param name="upperBound">The highest value the result may take, if any.</param>
        /// <param name="options">Whether a bound clamps the result or rejects the increment.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<StringIncrementResult<double>> Increment(
            this in RespStrings strings,
            RedisKey key,
            double value,
            Expiration expiry,
            double? lowerBound = null,
            double? upperBound = null,
            IncrementOptions options = IncrementOptions.None,
            CommandFlags flags = CommandFlags.None)
        {
            ValidateIncrementExpiry(expiry);

            var cmd = strings.Context.Compose(RedisCommand.INCREX, argHint: 9);
            try
            {
                cmd.Append($"{key}{RespLiterals.ByFloat}{value}");
                if (lowerBound.HasValue) cmd.Append($"{RespLiterals.LBound}{lowerBound.GetValueOrDefault()}");
                if (upperBound.HasValue) cmd.Append($"{RespLiterals.UBound}{upperBound.GetValueOrDefault()}");
                cmd.Append($"{AsFragment(options)}{expiry}");
            }
            catch
            {
                cmd.Dispose();
                throw;
            }

            var frame = cmd.Complete();
            return strings.Context.SendAsync(ref frame, flags.WithDefaultCategory(RedisCommand.INCREX), RespHandlers.Inbuilt<StringIncrementResult<double>>.Require());
        }

        /// <summary>LCS: the longest common subsequence of two keys' values.</summary>
        /// <param name="strings">The string command group.</param>
        /// <param name="first">The first key.</param>
        /// <param name="second">The second key.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<string?> LongestCommonSubsequence(this in RespStrings strings, RedisKey first, RedisKey second, CommandFlags flags = CommandFlags.None)
            => strings.Context.SendAsync<string?>(
                $"{RedisCommand.LCS}{first}{second}", flags.WithDefaultCategory(RedisCommand.LCS));

        /// <summary>LCS ... LEN: the length of the longest common subsequence, without transferring it.</summary>
        /// <param name="strings">The string command group.</param>
        /// <param name="first">The first key.</param>
        /// <param name="second">The second key.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<long> LongestCommonSubsequenceLength(this in RespStrings strings, RedisKey first, RedisKey second, CommandFlags flags = CommandFlags.None)
            => strings.Context.SendAsync<long>(
                $"{RedisCommand.LCS}{first}{second}{RespLiterals.Len}", flags.WithDefaultCategory(RedisCommand.LCS));

        /// <summary>LCS ... IDX: where the matches are, rather than what they contain.</summary>
        /// <param name="strings">The string command group.</param>
        /// <param name="first">The first key.</param>
        /// <param name="second">The second key.</param>
        /// <param name="minLength">Matches shorter than this are not reported.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<LCSMatchResult> LongestCommonSubsequenceWithMatches(
            this in RespStrings strings,
            RedisKey first,
            RedisKey second,
            long minLength = 0,
            CommandFlags flags = CommandFlags.None)
            => strings.Context.SendAsync(
                $"{RedisCommand.LCS}{first}{second}{RespLiterals.Idx}{RespLiterals.MinMatchLen}{minLength}{RespLiterals.WithMatchLen}",
                flags.WithDefaultCategory(RedisCommand.LCS),
                RespHandlers.Inbuilt<LCSMatchResult>.Require());

        /// <summary>
        /// Reject a condition this command has no spelling for, reusing <c>ValueCondition</c>'s own
        /// message so the two surfaces say the same thing.
        /// </summary>
        /// <typeparam name="T">The return type of the call site, which never receives a value.</typeparam>
        private static T ThrowUnsupportedCondition<T>(in ValueCondition when, string operation)
        {
            when.ThrowInvalidOperation(operation);
            return default!; // not reached; ThrowInvalidOperation always throws
        }

        /// <summary>The <c>SATURATE</c> token, or nothing; an unknown option is a mistake, not a no-op.</summary>
        private static RespFragment AsFragment(IncrementOptions options) => options switch
        {
            IncrementOptions.None => default, // a zero-argument fragment: written, contributes nothing
            IncrementOptions.Saturate => RespLiterals.Saturate,
            _ => throw new ArgumentOutOfRangeException(nameof(options)),
        };

        /// <summary>
        /// The expirations <c>INCREX</c> has no spelling for. Mirrors <c>RedisDatabase.ValidateStringIncrementExpiry</c>,
        /// which is the same list for the same command.
        /// </summary>
        private static void ValidateIncrementExpiry(Expiration expiry)
        {
            if (expiry.IsKeepTtl) throw new ArgumentException("KEEPTTL is not supported by this operation.", nameof(expiry));
            if (expiry.IsPersist) throw new ArgumentException("PERSIST is not supported by this operation.", nameof(expiry));
            if (expiry.IsExpireIfNotExists && !(expiry.IsAbsolute || expiry.IsRelative))
            {
                throw new ArgumentException("ENX requires EX, PX, EXAT, or PXAT.", nameof(expiry));
            }
        }
    }
}
