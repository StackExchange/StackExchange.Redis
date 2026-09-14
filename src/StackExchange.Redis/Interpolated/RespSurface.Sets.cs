using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using RESPite;

namespace StackExchange.Redis.Interpolated
{
    /// <summary>
    /// EXPERIMENTAL SPIKE. The set-command group: <c>target.Sets.Add(...)</c>.
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
        extension(IRespTarget target)
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
#pragma warning disable RS0026 // the one/many overloads are disambiguated by the second parameter
        public static ValueTask<bool> Add(this in RespSets sets, RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
#pragma warning restore RS0026
            => sets.Context.SendAsync<bool>(
                $"{RedisCommand.SADD}{key}{value}", flags.WithDefaultCategory(RedisCommand.SADD));

        /// <summary>SADD with several members; the reply is how many were new.</summary>
        /// <param name="sets">The set command group.</param>
        /// <param name="key">The key to write.</param>
        /// <param name="values">The members to add.</param>
        /// <param name="flags">Command flags.</param>
#pragma warning disable RS0026 // the one/many overloads are disambiguated by the second parameter
        public static ValueTask<long> Add(this in RespSets sets, RedisKey key, ReadOnlySpan<RedisValue> values, CommandFlags flags = CommandFlags.None)
#pragma warning restore RS0026
            => values.IsEmpty
                ? new ValueTask<long>(0L)
                : sets.Context.SendAsync<long>(
                    $"{RedisCommand.SADD}{key}{values}", flags.WithDefaultCategory(RedisCommand.SADD));

        /// <summary>SREM.</summary>
        /// <param name="sets">The set command group.</param>
        /// <param name="key">The key to write.</param>
        /// <param name="value">The member to remove.</param>
        /// <param name="flags">Command flags.</param>
#pragma warning disable RS0026 // the one/many overloads are disambiguated by the second parameter
        public static ValueTask<bool> Remove(this in RespSets sets, RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
#pragma warning restore RS0026
            => sets.Context.SendAsync<bool>(
                $"{RedisCommand.SREM}{key}{value}", flags.WithDefaultCategory(RedisCommand.SREM));

        /// <summary>SREM with several members; the reply is how many were removed.</summary>
        /// <param name="sets">The set command group.</param>
        /// <param name="key">The key to write.</param>
        /// <param name="values">The members to remove.</param>
        /// <param name="flags">Command flags.</param>
#pragma warning disable RS0026 // the one/many overloads are disambiguated by the second parameter
        public static ValueTask<long> Remove(this in RespSets sets, RedisKey key, ReadOnlySpan<RedisValue> values, CommandFlags flags = CommandFlags.None)
#pragma warning restore RS0026
            => values.IsEmpty
                ? new ValueTask<long>(0L)
                : sets.Context.SendAsync<long>(
                    $"{RedisCommand.SREM}{key}{values}", flags.WithDefaultCategory(RedisCommand.SREM));

        /// <summary>SISMEMBER.</summary>
        /// <param name="sets">The set command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="value">The member to look for.</param>
        /// <param name="flags">Command flags.</param>
#pragma warning disable RS0026 // the one/many overloads are disambiguated by the second parameter
        public static ValueTask<bool> Contains(this in RespSets sets, RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
#pragma warning restore RS0026
            => sets.Context.SendAsync<bool>(
                $"{RedisCommand.SISMEMBER}{key}{value}", flags.WithDefaultCategory(RedisCommand.SISMEMBER));

        /// <summary>SMISMEMBER: one answer per member, in order.</summary>
        /// <param name="sets">The set command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="values">The members to look for.</param>
        /// <param name="flags">Command flags.</param>
#pragma warning disable RS0026 // the one/many overloads are disambiguated by the second parameter
        public static ValueTask<bool[]> Contains(this in RespSets sets, RedisKey key, ReadOnlySpan<RedisValue> values, CommandFlags flags = CommandFlags.None)
#pragma warning restore RS0026
            => values.IsEmpty
                ? new ValueTask<bool[]>(Array.Empty<bool>())
                : sets.Context.SendAsync<bool[]>(
                    $"{RedisCommand.SMISMEMBER}{key}{values}", flags.WithDefaultCategory(RedisCommand.SMISMEMBER));

        /// <summary>SCARD.</summary>
        /// <param name="sets">The set command group.</param>
        /// <param name="key">The key to measure.</param>
        /// <param name="flags">Command flags.</param>
#pragma warning disable RS0026 // the set group's members share names with other groups' extension methods, but not receiver types
        public static ValueTask<long> Length(this in RespSets sets, RedisKey key, CommandFlags flags = CommandFlags.None)
#pragma warning restore RS0026
            => sets.Context.SendAsync<long>(
                $"{RedisCommand.SCARD}{key}", flags.WithDefaultCategory(RedisCommand.SCARD));

        /// <summary>SMEMBERS.</summary>
        /// <param name="sets">The set command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<RedisValue[]> Members(this in RespSets sets, RedisKey key, CommandFlags flags = CommandFlags.None)
            => sets.Context.SendAsync<RedisValue[]>(
                $"{RedisCommand.SMEMBERS}{key}", flags.WithDefaultCategory(RedisCommand.SMEMBERS));

        /// <summary>SMOVE.</summary>
        /// <param name="sets">The set command group.</param>
        /// <param name="source">The key to take from.</param>
        /// <param name="destination">The key to add to.</param>
        /// <param name="value">The member to move.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<bool> Move(this in RespSets sets, RedisKey source, RedisKey destination, RedisValue value, CommandFlags flags = CommandFlags.None)
            => sets.Context.SendAsync<bool>(
                $"{RedisCommand.SMOVE}{source}{destination}{value}", flags.WithDefaultCategory(RedisCommand.SMOVE));

        /// <summary>SPOP: remove and return one member, at random.</summary>
        /// <param name="sets">The set command group.</param>
        /// <param name="key">The key to write.</param>
        /// <param name="flags">Command flags.</param>
#pragma warning disable RS0026 // the one/many overloads are disambiguated by the second parameter
        public static ValueTask<RedisValue> Pop(this in RespSets sets, RedisKey key, CommandFlags flags = CommandFlags.None)
#pragma warning restore RS0026
            => sets.Context.SendAsync<RedisValue>(
                $"{RedisCommand.SPOP}{key}", flags.WithDefaultCategory(RedisCommand.SPOP));

        /// <summary>SPOP with a count.</summary>
        /// <param name="sets">The set command group.</param>
        /// <param name="key">The key to write.</param>
        /// <param name="count">How many to remove.</param>
        /// <param name="flags">Command flags.</param>
        /// <remarks>
        /// A count of zero removes nothing, and says so without asking - unlike the old surface, which
        /// sends a bare <c>SPOP</c> and would remove <b>one</b>. That is a divergence, and a deliberate
        /// one: "pop none" quietly popping one is the kind of thing a caller discovers in production.
        /// </remarks>
#pragma warning disable RS0026 // the one/many overloads are disambiguated by the second parameter
        public static ValueTask<RedisValue[]> Pop(this in RespSets sets, RedisKey key, long count, CommandFlags flags = CommandFlags.None)
#pragma warning restore RS0026
            => count == 0
                ? new ValueTask<RedisValue[]>(Array.Empty<RedisValue>())
                : sets.Context.SendAsync<RedisValue[]>(
                    $"{RedisCommand.SPOP}{key}{count}", flags.WithDefaultCategory(RedisCommand.SPOP));

        /// <summary>SRANDMEMBER: one member, at random, left in place.</summary>
        /// <param name="sets">The set command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<RedisValue> RandomMember(this in RespSets sets, RedisKey key, CommandFlags flags = CommandFlags.None)
            => sets.Context.SendAsync<RedisValue>(
                $"{RedisCommand.SRANDMEMBER}{key}", flags.WithDefaultCategory(RedisCommand.SRANDMEMBER));

        /// <summary>SRANDMEMBER with a count.</summary>
        /// <param name="sets">The set command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="count">How many to take; a negative count allows repeats.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<RedisValue[]> RandomMembers(this in RespSets sets, RedisKey key, long count, CommandFlags flags = CommandFlags.None)
            => sets.Context.SendAsync<RedisValue[]>(
                $"{RedisCommand.SRANDMEMBER}{key}{count}", flags.WithDefaultCategory(RedisCommand.SRANDMEMBER));

        /// <summary>SUNION/SINTER/SDIFF.</summary>
        /// <param name="sets">The set command group.</param>
        /// <param name="operation">The operation to apply.</param>
        /// <param name="keys">The keys to combine.</param>
        /// <param name="flags">Command flags.</param>
        /// <remarks>
        /// One method where the old surface has two: the <c>(first, second)</c> overload existed because
        /// building a variadic message used to be work, and with a run of keys as a hole it is the same
        /// expression either way.
        /// </remarks>
        public static ValueTask<RedisValue[]> Combine(this in RespSets sets, SetOperation operation, ReadOnlySpan<RedisKey> keys, CommandFlags flags = CommandFlags.None)
        {
            if (keys.IsEmpty) throw new ArgumentException("At least one key is required.", nameof(keys));

            var command = operation.ToSetCommand();
            return sets.Context.SendAsync<RedisValue[]>($"{command}{keys}", flags.WithDefaultCategory(command));
        }

        /// <summary>SUNIONSTORE/SINTERSTORE/SDIFFSTORE; the reply is the destination's size.</summary>
        /// <param name="sets">The set command group.</param>
        /// <param name="operation">The operation to apply.</param>
        /// <param name="destination">The key to write the result to.</param>
        /// <param name="keys">The keys to combine.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<long> CombineAndStore(this in RespSets sets, SetOperation operation, RedisKey destination, ReadOnlySpan<RedisKey> keys, CommandFlags flags = CommandFlags.None)
        {
            if (keys.IsEmpty) throw new ArgumentException("At least one key is required.", nameof(keys));

            var command = operation.ToSetStoreCommand();
            return sets.Context.SendAsync<long>($"{command}{destination}{keys}", flags.WithDefaultCategory(command));
        }

        /// <summary>SINTERCARD/SUNIONCARD/SDIFFCARD: the size of a combination, without building it.</summary>
        /// <param name="sets">The set command group.</param>
        /// <param name="operation">The operation to measure.</param>
        /// <param name="keys">The keys to combine.</param>
        /// <param name="limit">Stop counting at this many; zero for no limit.</param>
        /// <param name="approximate">Allow an estimate, where the server supports one.</param>
        /// <param name="flags">Command flags.</param>
        /// <remarks>
        /// <paramref name="approximate"/> is deliberately not gated here. Today only <c>SUNIONCARD</c>
        /// accepts <c>APPROX</c> and the others will error - but a stale client-side check would block a
        /// later server that extended it, so the server decides. Same reasoning as <c>RedisDatabase</c>.
        /// </remarks>
        public static ValueTask<long> CombineLength(
            this in RespSets sets,
            SetOperation operation,
            ReadOnlySpan<RedisKey> keys,
            long limit = 0,
            bool approximate = false,
            CommandFlags flags = CommandFlags.None)
        {
            if (keys.IsEmpty) throw new ArgumentException("At least one key is required.", nameof(keys));

            // numkeys comes FIRST here, unlike the plain combinations - the trailing LIMIT/APPROX operands
            // are why the server needs to be told where the key list stops
            var command = operation.ToSetCardinalityCommand();
            return sets.Context.SendAsync<long>(
                $"{command}{keys.Length}{keys}{(approximate ? RespLiterals.Approx : default)}{new RespLimit(limit)}",
                flags.WithDefaultCategory(command));
        }
    }

    /// <summary>
    /// EXPERIMENTAL SPIKE. The <c>LIMIT n</c> operand, which writes two arguments or none.
    /// </summary>
    /// <remarks>
    /// A fragment cannot spell this: the keyword is a constant but the count is not, and a hole writes one
    /// thing. <see cref="IRespArgument"/> is the mechanism for exactly that - it writes as many arguments
    /// as it likes, including none, which is how an absent optional operand is spelled throughout this
    /// surface. Zero means "no limit", and no limit means the operand is simply not there.
    /// </remarks>
    internal readonly struct RespLimit(long limit) : IRespArgument
    {
        /// <inheritdoc/>
        public void WriteTo(scoped ref RespCommandHandler handler)
        {
            if (limit <= 0) return;

            handler.AppendFormatted(RespLiterals.Limit);
            handler.AppendFormatted((RedisValue)limit);
        }
    }
}
