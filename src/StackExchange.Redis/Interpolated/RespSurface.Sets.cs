using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using RESPite;
using RESPite.Messages;

namespace StackExchange.Redis.Interpolated
{
    /// <summary>
    /// EXPERIMENTAL SPIKE. The set-command group: <c>target.Sets.AddAsync(...)</c>.
    /// </summary>
    /// <remarks>
    /// The smallest group so far, and the one where the variadic hole does most of the work: nearly every
    /// command here takes either a run of members or a run of keys, and the old surface spells each of
    /// those as a pair of overloads - one fixed-arity, one array. <c>SSCAN</c> stays with the other
    /// cursors.
    /// </remarks>
    [Experimental(Experiments.InterpolatedWriter, UrlFormat = Experiments.UrlFormat)]
    public readonly struct RespSets
    {
        private readonly RespContext _context;

        /// <summary>Group the set commands of a context.</summary>
        /// <param name="context">The context to send through.</param>
        public RespSets(in RespContext context) => _context = context;

        /// <summary>The underlying context.</summary>
        public RespContext Context => _context;
    }

    public static partial class RespSurface
    {
        extension(IRespKeyspaceTarget target)
        {
            /// <summary>The set commands.</summary>
            public RespSets Sets => new(target.Context);
        }

        extension(in RespContext context)
        {
            /// <summary>The set commands.</summary>
            public RespSets Sets => new(context);
        }

        /// <summary>SADD.</summary>
        /// <param name="sets">The set command group.</param>
        /// <param name="key">The key to write.</param>
        /// <param name="value">The member to add.</param>
        /// <param name="flags">Command flags.</param>
        /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
        public static ValueTask<bool> AddAsync(this in RespSets sets, RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
            => sets.Context.SendAsync<bool>(
                $"{RedisCommand.SADD}{key}{value}", flags, cancellationToken: cancellationToken);

        /// <summary>SADD with several members; the reply is how many were new.</summary>
        /// <param name="sets">The set command group.</param>
        /// <param name="key">The key to write.</param>
        /// <param name="values">The members to add.</param>
        /// <param name="flags">Command flags.</param>
        /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
        public static ValueTask<long> AddAsync(this in RespSets sets, RedisKey key, ReadOnlySpan<RedisValue> values, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
            => values.IsEmpty
                ? new ValueTask<long>(0L)
                : sets.Context.SendAsync<long>(
                    $"{RedisCommand.SADD}{key}{values}", flags, cancellationToken: cancellationToken);

        /// <summary>SREM.</summary>
        /// <param name="sets">The set command group.</param>
        /// <param name="key">The key to write.</param>
        /// <param name="value">The member to remove.</param>
        /// <param name="flags">Command flags.</param>
        /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
        public static ValueTask<bool> RemoveAsync(this in RespSets sets, RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
            => sets.Context.SendAsync<bool>(
                $"{RedisCommand.SREM}{key}{value}", flags, cancellationToken: cancellationToken);

        /// <summary>SREM with several members; the reply is how many were removed.</summary>
        /// <param name="sets">The set command group.</param>
        /// <param name="key">The key to write.</param>
        /// <param name="values">The members to remove.</param>
        /// <param name="flags">Command flags.</param>
        /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
        public static ValueTask<long> RemoveAsync(this in RespSets sets, RedisKey key, ReadOnlySpan<RedisValue> values, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
            => values.IsEmpty
                ? new ValueTask<long>(0L)
                : sets.Context.SendAsync<long>(
                    $"{RedisCommand.SREM}{key}{values}", flags, cancellationToken: cancellationToken);

        /// <summary>SISMEMBER.</summary>
        /// <param name="sets">The set command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="value">The member to look for.</param>
        /// <param name="flags">Command flags.</param>
        /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
        public static ValueTask<bool> ContainsAsync(this in RespSets sets, RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
            => sets.Context.SendAsync<bool>(
                $"{RedisCommand.SISMEMBER}{key}{value}", flags, cancellationToken: cancellationToken);

        /// <summary>SMISMEMBER: one answer per member, in order.</summary>
        /// <param name="sets">The set command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="values">The members to look for.</param>
        /// <param name="flags">Command flags.</param>
        /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
        public static ValueTask<ReadOnlyLease<bool>> ContainsAsync(this in RespSets sets, RedisKey key, ReadOnlySpan<RedisValue> values, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
            => values.IsEmpty
                ? new ValueTask<ReadOnlyLease<bool>>(ReadOnlyLease<bool>.Empty)
                : sets.Context.SendAsync<ReadOnlyLease<bool>>(
                    $"{RedisCommand.SMISMEMBER}{key}{values}", flags, cancellationToken: cancellationToken);

        /// <summary>Contains, as an array, for the old <c>IDatabase</c> surface.</summary>
        /// <remarks>
        /// Internal sibling of <c>Contains</c>. A sibling rather than a conversion: <c>IDatabase</c> promises
        /// an array the caller owns, so going via the lease would rent a pooled buffer only to copy out of
        /// it.
        /// <para>
        /// <b>Permanent, not scaffolding.</b> <c>IDatabase</c> is not going anywhere - compatibility
        /// outranks tidiness here - so this is how that signature is served from the new core, for as long
        /// as the signature exists. Internal because the array is the <i>old</i> spelling: new code should
        /// reach for the lease, and nothing outside this assembly should be able to choose otherwise.
        /// </para>
        /// </remarks>
        internal static ValueTask<bool[]> ContainsArray(this in RespSets sets, RedisKey key, ReadOnlySpan<RedisValue> values, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
            => values.IsEmpty
                ? new ValueTask<bool[]>(Array.Empty<bool>())
                : sets.Context.SendAsync<bool[]>(
                    $"{RedisCommand.SMISMEMBER}{key}{values}", flags, cancellationToken: cancellationToken);

        /// <summary>SCARD.</summary>
        /// <param name="sets">The set command group.</param>
        /// <param name="key">The key to measure.</param>
        /// <param name="flags">Command flags.</param>
        /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
        public static ValueTask<long> LengthAsync(this in RespSets sets, RedisKey key, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
            => sets.Context.SendAsync<long>(
                $"{RedisCommand.SCARD}{key}", flags, cancellationToken: cancellationToken);

        /// <summary>SMEMBERS.</summary>
        /// <param name="sets">The set command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="flags">Command flags.</param>
        /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
        public static ValueTask<ReadOnlyLease<RespValue>> MembersAsync(this in RespSets sets, RedisKey key, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
            => sets.Context.SendAsync<ReadOnlyLease<RespValue>>(
                $"{RedisCommand.SMEMBERS}{key}", flags, cancellationToken: cancellationToken);

        /// <summary>Members, as an array, for the old <c>IDatabase</c> surface.</summary>
        /// <remarks>
        /// Internal sibling of <c>Members</c>. A sibling rather than a conversion: <c>IDatabase</c> promises
        /// an array the caller owns, so going via the lease would rent a pooled buffer only to copy out of
        /// it.
        /// <para>
        /// <b>Permanent, not scaffolding.</b> <c>IDatabase</c> is not going anywhere - compatibility
        /// outranks tidiness here - so this is how that signature is served from the new core, for as long
        /// as the signature exists. Internal because the array is the <i>old</i> spelling: new code should
        /// reach for the lease, and nothing outside this assembly should be able to choose otherwise.
        /// </para>
        /// </remarks>
        internal static ValueTask<RedisValue[]> MembersArray(this in RespSets sets, RedisKey key, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
            => sets.Context.SendAsync<RedisValue[]>(
                $"{RedisCommand.SMEMBERS}{key}", flags, cancellationToken: cancellationToken);

        /// <summary>SMOVE.</summary>
        /// <param name="sets">The set command group.</param>
        /// <param name="source">The key to take from.</param>
        /// <param name="destination">The key to add to.</param>
        /// <param name="value">The member to move.</param>
        /// <param name="flags">Command flags.</param>
        /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
        public static ValueTask<bool> MoveAsync(this in RespSets sets, RedisKey source, RedisKey destination, RedisValue value, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
            => sets.Context.SendAsync<bool>(
                $"{RedisCommand.SMOVE}{source}{destination}{value}", flags, cancellationToken: cancellationToken);

        /// <summary>SPOP: remove and return one member, at random.</summary>
        /// <param name="sets">The set command group.</param>
        /// <param name="key">The key to write.</param>
        /// <param name="flags">Command flags.</param>
        /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
        public static ValueTask<RedisValue> PopAsync(this in RespSets sets, RedisKey key, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
            => sets.Context.SendAsync<RedisValue>(
                $"{RedisCommand.SPOP}{key}", flags, cancellationToken: cancellationToken);

        /// <summary>SPOP with a count.</summary>
        /// <param name="sets">The set command group.</param>
        /// <param name="key">The key to write.</param>
        /// <param name="count">How many to remove.</param>
        /// <param name="flags">Command flags.</param>
        /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
        /// <remarks>
        /// A count of zero removes nothing, and says so without asking - unlike the old surface, which
        /// sends a bare <c>SPOP</c> and would remove <b>one</b>. That is a divergence, and a deliberate
        /// one: "pop none" quietly popping one is the kind of thing a caller discovers in production.
        /// </remarks>
        public static ValueTask<ReadOnlyLease<RespValue>> PopAsync(this in RespSets sets, RedisKey key, long count, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
            => count == 0
                ? new ValueTask<ReadOnlyLease<RespValue>>(ReadOnlyLease<RespValue>.Empty)
                : sets.Context.SendAsync<ReadOnlyLease<RespValue>>(
                    $"{RedisCommand.SPOP}{key}{count}", flags, cancellationToken: cancellationToken);

        /// <summary>Pop, as an array, for the old <c>IDatabase</c> surface.</summary>
        /// <remarks>
        /// Internal sibling of <c>Pop</c>. A sibling rather than a conversion: <c>IDatabase</c> promises
        /// an array the caller owns, so going via the lease would rent a pooled buffer only to copy out of
        /// it.
        /// <para>
        /// <b>Permanent, not scaffolding.</b> <c>IDatabase</c> is not going anywhere - compatibility
        /// outranks tidiness here - so this is how that signature is served from the new core, for as long
        /// as the signature exists. Internal because the array is the <i>old</i> spelling: new code should
        /// reach for the lease, and nothing outside this assembly should be able to choose otherwise.
        /// </para>
        /// </remarks>
        internal static ValueTask<RedisValue[]> PopArray(this in RespSets sets, RedisKey key, long count, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
            => count == 0
                ? new ValueTask<RedisValue[]>(Array.Empty<RedisValue>())
                : sets.Context.SendAsync<RedisValue[]>(
                    $"{RedisCommand.SPOP}{key}{count}", flags, cancellationToken: cancellationToken);

        /// <summary>SRANDMEMBER: one member, at random, left in place.</summary>
        /// <param name="sets">The set command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="flags">Command flags.</param>
        /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
        public static ValueTask<RedisValue> RandomMemberAsync(this in RespSets sets, RedisKey key, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
            => sets.Context.SendAsync<RedisValue>(
                $"{RedisCommand.SRANDMEMBER}{key}", flags.NeverCached(), cancellationToken: cancellationToken);

        /// <summary>SRANDMEMBER with a count.</summary>
        /// <param name="sets">The set command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="count">How many to take; a negative count allows repeats.</param>
        /// <param name="flags">Command flags.</param>
        /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
        public static ValueTask<ReadOnlyLease<RespValue>> RandomMembersAsync(this in RespSets sets, RedisKey key, long count, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
            => sets.Context.SendAsync<ReadOnlyLease<RespValue>>(
                $"{RedisCommand.SRANDMEMBER}{key}{count}", flags.NeverCached(), cancellationToken: cancellationToken);

        /// <summary>RandomMembers, as an array, for the old <c>IDatabase</c> surface.</summary>
        /// <remarks>
        /// Internal sibling of <c>RandomMembers</c>. A sibling rather than a conversion: <c>IDatabase</c> promises
        /// an array the caller owns, so going via the lease would rent a pooled buffer only to copy out of
        /// it.
        /// <para>
        /// <b>Permanent, not scaffolding.</b> <c>IDatabase</c> is not going anywhere - compatibility
        /// outranks tidiness here - so this is how that signature is served from the new core, for as long
        /// as the signature exists. Internal because the array is the <i>old</i> spelling: new code should
        /// reach for the lease, and nothing outside this assembly should be able to choose otherwise.
        /// </para>
        /// </remarks>
        internal static ValueTask<RedisValue[]> RandomMembersArray(this in RespSets sets, RedisKey key, long count, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
            => sets.Context.SendAsync<RedisValue[]>(
                $"{RedisCommand.SRANDMEMBER}{key}{count}", flags.NeverCached(), cancellationToken: cancellationToken);

        /// <summary>SUNION/SINTER/SDIFF.</summary>
        /// <param name="sets">The set command group.</param>
        /// <param name="operation">The operation to apply.</param>
        /// <param name="keys">The keys to combine.</param>
        /// <param name="flags">Command flags.</param>
        /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
        /// <remarks>
        /// One method where the old surface has two: the <c>(first, second)</c> overload existed because
        /// building a variadic message used to be work, and with a run of keys as a hole it is the same
        /// expression either way.
        /// </remarks>
        public static ValueTask<ReadOnlyLease<RespValue>> CombineAsync(this in RespSets sets, SetOperation operation, ReadOnlySpan<RedisKey> keys, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        {
            if (keys.IsEmpty) throw new ArgumentException("At least one key is required.", nameof(keys));

            var command = operation.ToSetCommand();
            return sets.Context.SendAsync<ReadOnlyLease<RespValue>>($"{command}{keys}", flags, cancellationToken: cancellationToken);
        }

        /// <summary>Combine, as an array, for the old <c>IDatabase</c> surface.</summary>
        /// <remarks>
        /// Internal sibling of <c>Combine</c>. A sibling rather than a conversion: <c>IDatabase</c> promises
        /// an array the caller owns, so going via the lease would rent a pooled buffer only to copy out of
        /// it.
        /// <para>
        /// <b>Permanent, not scaffolding.</b> <c>IDatabase</c> is not going anywhere - compatibility
        /// outranks tidiness here - so this is how that signature is served from the new core, for as long
        /// as the signature exists. Internal because the array is the <i>old</i> spelling: new code should
        /// reach for the lease, and nothing outside this assembly should be able to choose otherwise.
        /// </para>
        /// </remarks>
        internal static ValueTask<RedisValue[]> CombineArray(this in RespSets sets, SetOperation operation, ReadOnlySpan<RedisKey> keys, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        {
            if (keys.IsEmpty) throw new ArgumentException("At least one key is required.", nameof(keys));

            var command = operation.ToSetCommand();
            return sets.Context.SendAsync<RedisValue[]>($"{command}{keys}", flags, cancellationToken: cancellationToken);
        }

        /// <summary>SUNIONSTORE/SINTERSTORE/SDIFFSTORE; the reply is the destination's size.</summary>
        /// <param name="sets">The set command group.</param>
        /// <param name="operation">The operation to apply.</param>
        /// <param name="destination">The key to write the result to.</param>
        /// <param name="keys">The keys to combine.</param>
        /// <param name="flags">Command flags.</param>
        /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
        public static ValueTask<long> CombineAndStoreAsync(this in RespSets sets, SetOperation operation, RedisKey destination, ReadOnlySpan<RedisKey> keys, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        {
            if (keys.IsEmpty) throw new ArgumentException("At least one key is required.", nameof(keys));

            var command = operation.ToSetStoreCommand();
            return sets.Context.SendAsync<long>($"{command}{destination}{keys}", flags, cancellationToken: cancellationToken);
        }

        /// <summary>SINTERCARD/SUNIONCARD/SDIFFCARD: the size of a combination, without building it.</summary>
        /// <param name="sets">The set command group.</param>
        /// <param name="operation">The operation to measure.</param>
        /// <param name="keys">The keys to combine.</param>
        /// <param name="limit">Stop counting at this many; zero for no limit.</param>
        /// <param name="approximate">Allow an estimate, where the server supports one.</param>
        /// <param name="flags">Command flags.</param>
        /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
        /// <remarks>
        /// <paramref name="approximate"/> is deliberately not gated here. Today only <c>SUNIONCARD</c>
        /// accepts <c>APPROX</c> and the others will error - but a stale client-side check would block a
        /// later server that extended it, so the server decides. Same reasoning as <c>RedisDatabase</c>.
        /// </remarks>
        public static ValueTask<long> CombineLengthAsync(
            this in RespSets sets,
            SetOperation operation,
            ReadOnlySpan<RedisKey> keys,
            long? limit = null,
            bool approximate = false,
            CommandFlags flags = CommandFlags.None,
            CancellationToken cancellationToken = default)
        {
            if (keys.IsEmpty) throw new ArgumentException("At least one key is required.", nameof(keys));

            // numkeys comes FIRST here, unlike the plain combinations - the trailing LIMIT/APPROX operands
            // are why the server needs to be told where the key list stops
            var command = operation.ToSetCardinalityCommand();
            return sets.Context.SendAsync<long>(
                $"{command}{keys.Length}{keys}{RespLiterals.Approx.When(approximate)}{RespLiterals.Limit.When(limit)}{limit}",
                flags,
                cancellationToken: cancellationToken);
        }
    }
}
