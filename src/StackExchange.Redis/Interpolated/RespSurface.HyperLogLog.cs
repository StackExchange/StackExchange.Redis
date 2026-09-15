using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using RESPite;

namespace StackExchange.Redis.Interpolated
{
    /// <summary>
    /// EXPERIMENTAL SPIKE. The HyperLogLog group: <c>target.HyperLogLog.AddAsync(...)</c>.
    /// </summary>
    /// <remarks>
    /// Three commands, and the whole group would be unremarkable but for <c>PFCOUNT</c>: it rewrites the
    /// cached cardinality into the key, so before 2.8.18 it is a write however much it reads. That is a
    /// routing decision rather than a spelling one - the same bytes go out either way - which makes it the
    /// first user of <see cref="IRespServerFeatures"/> that changes the flags instead of the command.
    /// </remarks>
    [Experimental(Experiments.InterpolatedWriter, UrlFormat = Experiments.UrlFormat)]
    public readonly struct RespHyperLogLog
    {
        private readonly RespContext _context;

        /// <summary>Group the HyperLogLog commands of a context.</summary>
        /// <param name="context">The context to send through.</param>
        public RespHyperLogLog(in RespContext context) => _context = context;

        /// <summary>The underlying context.</summary>
        public RespContext Context => _context;
    }

    public static partial class RespSurface
    {
        extension(IRespKeyspaceTarget target)
        {
            /// <summary>The HyperLogLog commands.</summary>
            public RespHyperLogLog HyperLogLog => new(target.Context);
        }

        extension(in RespContext context)
        {
            /// <summary>The HyperLogLog commands.</summary>
            public RespHyperLogLog HyperLogLog => new(context);
        }

        /// <summary>PFADD; the reply is whether the estimate changed.</summary>
        /// <param name="log">The HyperLogLog command group.</param>
        /// <param name="key">The key to write.</param>
        /// <param name="value">The element to observe.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<bool> AddAsync(this in RespHyperLogLog log, RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
            => log.Context.SendAsync<bool>(
                $"{RedisCommand.PFADD}{key}{value}", flags.WithDefaultCategory(RedisCommand.PFADD));

        /// <summary>PFADD with several elements at once.</summary>
        /// <param name="log">The HyperLogLog command group.</param>
        /// <param name="key">The key to write.</param>
        /// <param name="values">The elements to observe.</param>
        /// <param name="flags">Command flags.</param>
        /// <remarks>
        /// An empty run is <b>not</b> short-circuited, unlike <c>SADD</c>: <c>PFADD key</c> with no
        /// elements is a request in its own right - it creates an empty structure and reports whether it
        /// had to - so there is a real answer to give and nothing to guess.
        /// </remarks>
        public static ValueTask<bool> AddAsync(this in RespHyperLogLog log, RedisKey key, ReadOnlySpan<RedisValue> values, CommandFlags flags = CommandFlags.None)
            => log.Context.SendAsync<bool>(
                $"{RedisCommand.PFADD}{key}{values}", flags.WithDefaultCategory(RedisCommand.PFADD));

        /// <summary>PFCOUNT: the approximate cardinality.</summary>
        /// <param name="log">The HyperLogLog command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="flags">Command flags.</param>
        /// <remarks><inheritdoc cref="RespHyperLogLog" path="/remarks"/></remarks>
        public static ValueTask<long> LengthAsync(this in RespHyperLogLog log, RedisKey key, CommandFlags flags = CommandFlags.None)
            => log.Context.SendAsync<long>(
                $"{RedisCommand.PFCOUNT}{key}", CountFlags(log.Context, in key, flags));

        /// <summary>PFCOUNT over several keys: the cardinality of their union, without storing it.</summary>
        /// <param name="log">The HyperLogLog command group.</param>
        /// <param name="keys">The keys to read.</param>
        /// <param name="flags">Command flags.</param>
        /// <remarks><inheritdoc cref="RespHyperLogLog" path="/remarks"/></remarks>
        public static ValueTask<long> LengthAsync(this in RespHyperLogLog log, ReadOnlySpan<RedisKey> keys, CommandFlags flags = CommandFlags.None)
        {
            // routing follows the first key, as the old surface does; with no keys there is nothing to
            // route on, and the server will reject the arity anyway
            RedisKey routing = keys.IsEmpty ? default : keys[0];
            return log.Context.SendAsync<long>(
                $"{RedisCommand.PFCOUNT}{keys}", CountFlags(log.Context, in routing, flags));
        }

        /// <summary>PFMERGE: fold several structures into one.</summary>
        /// <param name="log">The HyperLogLog command group.</param>
        /// <param name="destination">The key to write the union to; it is included in the union.</param>
        /// <param name="sourceKeys">The keys to fold in.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask MergeAsync(this in RespHyperLogLog log, RedisKey destination, ReadOnlySpan<RedisKey> sourceKeys, CommandFlags flags = CommandFlags.None)
            => log.Context.SendAsync(
                $"{RedisCommand.PFMERGE}{destination}{sourceKeys}", flags.WithDefaultCategory(RedisCommand.PFMERGE));

        /// <summary>The flags a <c>PFCOUNT</c> should go out with, given what is known about the server.</summary>
        private static CommandFlags CountFlags(in RespContext context, in RedisKey key, CommandFlags flags)
        {
            flags = flags.WithDefaultCategory(RedisCommand.PFCOUNT);

            // only a KNOWN version demotes this. The old surface has the same rule for the same reason -
            // it demotes only when a server was actually selected - so a context that cannot ask keeps the
            // caller's routing, and the worst case is the one the caller asked for.
            return context.TryGetFeatures(RedisCommand.PFCOUNT, in key, flags, out var features)
                && !features.HyperLogLogCountReplicaSafe
                ? Message.DemandPrimary(flags, RedisCommand.PFCOUNT)
                : flags;
        }
    }
}
