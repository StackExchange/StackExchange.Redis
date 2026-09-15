using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using RESPite;

namespace StackExchange.Redis.Interpolated
{
    /// <summary>
    /// EXPERIMENTAL SPIKE. The key-command group: <c>target.Keys.Delete(...)</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The old surface spells these with a <c>Key</c> prefix - <c>KeyDelete</c>, <c>KeyExpire</c>,
    /// <c>KeyTimeToLive</c> - which was the only way to group them when everything hung off one interface.
    /// Here the group is the receiver, so the prefix goes and <c>Keys.Delete</c> says the same thing.
    /// </para>
    /// <para>
    /// Named <c>Keys</c> rather than <c>Keyspace</c>: it matches the other groups, which are all
    /// plural-of-the-thing, and <c>Keyspace</c> already means something else in this library - see the
    /// <c>KeyspaceIsolation</c> namespace, which is about prefixing rather than about key commands.
    /// </para>
    /// <para>
    /// <c>DBSIZE</c> is <b>not</b> here despite looking like it belongs: it is an <c>IServer</c> command,
    /// not a database one, and it lands in that context when it exists. The <c>OBJECT</c> family
    /// (<c>ENCODING</c>, <c>REFCOUNT</c>, <c>FREQ</c>, <c>IDLETIME</c>) is deferred for the same reason
    /// the scan cursors were: a different command shape, better done together.
    /// </para>
    /// </remarks>
    [Experimental(Experiments.InterpolatedWriter, UrlFormat = Experiments.UrlFormat)]
    public readonly struct RespKeys
    {
        private readonly RespContext _context;

        /// <summary>Group the key commands of a context.</summary>
        /// <param name="context">The context to send through.</param>
        public RespKeys(in RespContext context) => _context = context;

        /// <summary>The underlying context.</summary>
        public RespContext Context => _context;
    }

    public static partial class RespSurface
    {
        extension(IRespTarget target)
        {
            /// <summary>The key commands.</summary>
            public RespKeys Keys => new(target.Context);
        }

        extension(in RespContext context)
        {
            /// <summary>The key commands.</summary>
            public RespKeys Keys => new(context);
        }

        /// <summary>DEL: remove a key, reporting whether it was there.</summary>
        /// <param name="keys">The key command group.</param>
        /// <param name="key">The key to remove.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<bool> Delete(this in RespKeys keys, RedisKey key, CommandFlags flags = CommandFlags.None)
            => keys.Context.SendAsync<bool>(
                $"{RedisCommand.DEL}{key}", flags.WithDefaultCategory(RedisCommand.DEL));

        /// <summary>DEL with several keys; the reply is how many existed.</summary>
        /// <param name="keys">The key command group.</param>
        /// <param name="targets">The keys to remove.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<long> Delete(this in RespKeys keys, ReadOnlySpan<RedisKey> targets, CommandFlags flags = CommandFlags.None)
            => targets.IsEmpty
                ? new ValueTask<long>(0L)
                : keys.Context.SendAsync<long>(
                    $"{RedisCommand.DEL}{targets}", flags.WithDefaultCategory(RedisCommand.DEL));

        /// <summary>UNLINK: as <c>DEL</c>, but the reclaim happens on another thread.</summary>
        /// <param name="keys">The key command group.</param>
        /// <param name="key">The key to remove.</param>
        /// <param name="flags">Command flags.</param>
        /// <remarks>
        /// A separate method rather than a flag on <c>Delete</c>: the difference is visible to the server
        /// operator rather than to the caller, and hiding it behind an option would make the choice
        /// invisible at the call site, which is where it is made.
        /// </remarks>
        public static ValueTask<bool> Unlink(this in RespKeys keys, RedisKey key, CommandFlags flags = CommandFlags.None)
            => keys.Context.SendAsync<bool>(
                $"{RedisCommand.UNLINK}{key}", flags.WithDefaultCategory(RedisCommand.UNLINK));

        /// <inheritdoc cref="Unlink(in RespKeys, RedisKey, CommandFlags)"/>
        /// <param name="keys">The key command group.</param>
        /// <param name="targets">The keys to remove.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<long> Unlink(this in RespKeys keys, ReadOnlySpan<RedisKey> targets, CommandFlags flags = CommandFlags.None)
            => targets.IsEmpty
                ? new ValueTask<long>(0L)
                : keys.Context.SendAsync<long>(
                    $"{RedisCommand.UNLINK}{targets}", flags.WithDefaultCategory(RedisCommand.UNLINK));

        /// <summary>EXISTS.</summary>
        /// <param name="keys">The key command group.</param>
        /// <param name="key">The key to test.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<bool> Exists(this in RespKeys keys, RedisKey key, CommandFlags flags = CommandFlags.None)
            => keys.Context.SendAsync<bool>(
                $"{RedisCommand.EXISTS}{key}", flags.WithDefaultCategory(RedisCommand.EXISTS));

        /// <summary>EXISTS with several keys; the reply counts them, including duplicates.</summary>
        /// <param name="keys">The key command group.</param>
        /// <param name="targets">The keys to test.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<long> Exists(this in RespKeys keys, ReadOnlySpan<RedisKey> targets, CommandFlags flags = CommandFlags.None)
            => targets.IsEmpty
                ? new ValueTask<long>(0L)
                : keys.Context.SendAsync<long>(
                    $"{RedisCommand.EXISTS}{targets}", flags.WithDefaultCategory(RedisCommand.EXISTS));

        /// <summary>EXPIRE/PEXPIRE/EXPIREAT/PEXPIREAT, chosen from the expiry.</summary>
        /// <param name="keys">The key command group.</param>
        /// <param name="key">The key to set a deadline on.</param>
        /// <param name="expiry">When it should expire.</param>
        /// <param name="when">The condition under which the deadline applies.</param>
        /// <param name="flags">Command flags.</param>
        /// <remarks>
        /// One method over four commands, as in the hash group: whether the deadline is absolute or
        /// relative, and whether it is in seconds or milliseconds, are properties of the
        /// <see cref="Expiration"/> rather than decisions the caller should have to spell as a command name.
        /// <c>Persist</c> is deliberately not reachable from here - it is a different command with a
        /// different reply, and <see cref="Expiration.Persist"/> is rejected rather than silently rerouted.
        /// </remarks>
        public static ValueTask<bool> Expire(
            this in RespKeys keys,
            RedisKey key,
            Expiration expiry,
            ExpireWhen when = ExpireWhen.Always,
            CommandFlags flags = CommandFlags.None)
        {
            var command = SelectKeyExpireCommand(expiry);
            return keys.Context.SendAsync<bool>(
                $"{command}{key}{expiry.Value}{AsFragment(when)}",
                flags.WithRetryCategory(when.AsRetryCategory()).WithDefaultCategory(command));
        }

        /// <summary>PERSIST: remove any deadline.</summary>
        /// <param name="keys">The key command group.</param>
        /// <param name="key">The key to make permanent.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<bool> Persist(this in RespKeys keys, RedisKey key, CommandFlags flags = CommandFlags.None)
            => keys.Context.SendAsync<bool>(
                $"{RedisCommand.PERSIST}{key}", flags.WithDefaultCategory(RedisCommand.PERSIST));

        /// <summary>PTTL: how long the key has left, or null if it has no deadline.</summary>
        /// <param name="keys">The key command group.</param>
        /// <param name="key">The key to ask about.</param>
        /// <param name="flags">Command flags.</param>
        /// <remarks>
        /// <b>Never cached</b>, and it is the clearest case in the library: the answer counts down, so it is
        /// already wrong by the time it is stored, and no correction is coming because the server announces
        /// expiry to nobody. Contrast <see cref="ExpireTime"/>, which names an instant and does not drift.
        /// <para>
        /// "No such key" and "no deadline" both read as null - the caller asked how long is left, and the
        /// answer is "no deadline" either way. <see cref="Exists(in RespKeys, RedisKey, CommandFlags)"/>
        /// distinguishes them.
        /// </para>
        /// </remarks>
        public static ValueTask<TimeSpan?> TimeToLive(this in RespKeys keys, RedisKey key, CommandFlags flags = CommandFlags.None)
            => keys.Context.SendAsync<TimeSpan?>(
                $"{RedisCommand.PTTL}{key}", flags.WithDefaultCategory(RedisCommand.PTTL).NeverCached());

        /// <summary>PEXPIRETIME: when the key expires, or null if it has no deadline.</summary>
        /// <param name="keys">The key command group.</param>
        /// <param name="key">The key to ask about.</param>
        /// <param name="flags">Command flags.</param>
        /// <remarks>
        /// Cacheable where <see cref="TimeToLive"/> is not: an instant does not drift, so this only becomes
        /// wrong once the key actually expires - the same exposure every cached read of a volatile key
        /// already has, and what <see cref="CachePolicy.TimeToLive"/> exists to bound.
        /// </remarks>
        public static ValueTask<DateTime?> ExpireTime(this in RespKeys keys, RedisKey key, CommandFlags flags = CommandFlags.None)
            => keys.Context.SendAsync<DateTime?>(
                $"{RedisCommand.PEXPIRETIME}{key}", flags.WithDefaultCategory(RedisCommand.PEXPIRETIME));

        /// <summary>RENAME, or RENAMENX when the destination must not exist.</summary>
        /// <param name="keys">The key command group.</param>
        /// <param name="key">The key to rename.</param>
        /// <param name="newKey">The name to give it.</param>
        /// <param name="when">Whether an existing destination may be replaced.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<bool> Rename(
            this in RespKeys keys,
            RedisKey key,
            RedisKey newKey,
            When when = When.Always,
            CommandFlags flags = CommandFlags.None)
        {
            var command = when switch
            {
                When.Always => RedisCommand.RENAME,
                When.NotExists => RedisCommand.RENAMENX,
                _ => throw new ArgumentOutOfRangeException(nameof(when), when, "RENAME has no XX form."),
            };

            return keys.Context.SendAsync<bool>(
                $"{command}{key}{newKey}", flags.WithDefaultCategory(command));
        }

        /// <summary>TOUCH: mark a key as recently used, reporting whether it was there.</summary>
        /// <param name="keys">The key command group.</param>
        /// <param name="key">The key to touch.</param>
        /// <param name="flags">Command flags.</param>
        /// <remarks>
        /// <b>Never cached.</b> The point of the command is the side effect on the server's idle/LRU
        /// bookkeeping, so answering it locally would skip the only thing it was called for - and the reply
        /// it happens to return would then be a cached statement about existence with nothing to correct it.
        /// </remarks>
        public static ValueTask<bool> Touch(this in RespKeys keys, RedisKey key, CommandFlags flags = CommandFlags.None)
            => keys.Context.SendAsync<bool>(
                $"{RedisCommand.TOUCH}{key}", flags.WithDefaultCategory(RedisCommand.TOUCH).NeverCached());

        /// <inheritdoc cref="Touch(in RespKeys, RedisKey, CommandFlags)"/>
        /// <param name="keys">The key command group.</param>
        /// <param name="targets">The keys to touch.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<long> Touch(this in RespKeys keys, ReadOnlySpan<RedisKey> targets, CommandFlags flags = CommandFlags.None)
            => targets.IsEmpty
                ? new ValueTask<long>(0L)
                : keys.Context.SendAsync<long>(
                    $"{RedisCommand.TOUCH}{targets}", flags.WithDefaultCategory(RedisCommand.TOUCH).NeverCached());

        /// <summary>RANDOMKEY.</summary>
        /// <param name="keys">The key command group.</param>
        /// <param name="flags">Command flags.</param>
        /// <remarks>
        /// <b>Never cached</b>, for both available reasons at once: the answer is meant to differ each time,
        /// and the command names no key, so nothing could ever invalidate an entry for it. Either one alone
        /// would be enough.
        /// </remarks>
        public static ValueTask<RedisKey> Random(this in RespKeys keys, CommandFlags flags = CommandFlags.None)
            => keys.Context.SendAsync<RedisKey>(
                $"{RedisCommand.RANDOMKEY}", flags.WithDefaultCategory(RedisCommand.RANDOMKEY).NeverCached());

        /// <summary>TYPE.</summary>
        /// <param name="keys">The key command group.</param>
        /// <param name="key">The key to inspect.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<RedisType> Type(this in RespKeys keys, RedisKey key, CommandFlags flags = CommandFlags.None)
            => keys.Context.SendAsync<RedisType>(
                $"{RedisCommand.TYPE}{key}", flags.WithDefaultCategory(RedisCommand.TYPE));

        /// <summary>COPY.</summary>
        /// <param name="keys">The key command group.</param>
        /// <param name="source">The key to copy from.</param>
        /// <param name="destination">The key to copy to.</param>
        /// <param name="destinationDatabase">The database to copy into; -1 for the current one.</param>
        /// <param name="replace">Whether an existing destination may be overwritten.</param>
        /// <param name="flags">Command flags.</param>
        /// <remarks>
        /// The two optional operands are holes rather than branches: an absent <c>DB</c> and an absent
        /// <c>REPLACE</c> are zero-argument fragments, so one interpolated string covers all four shapes.
        /// </remarks>
        public static ValueTask<bool> Copy(
            this in RespKeys keys,
            RedisKey source,
            RedisKey destination,
            int destinationDatabase = -1,
            bool replace = false,
            CommandFlags flags = CommandFlags.None)
            => keys.Context.SendAsync<bool>(
                $"{RedisCommand.COPY}{source}{destination}{new DatabaseOperand(destinationDatabase)}{(replace ? RespLiterals.Replace : default)}",
                flags.WithDefaultCategory(RedisCommand.COPY));

        /// <summary>MOVE.</summary>
        /// <param name="keys">The key command group.</param>
        /// <param name="key">The key to move.</param>
        /// <param name="database">The database to move it into.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<bool> Move(this in RespKeys keys, RedisKey key, int database, CommandFlags flags = CommandFlags.None)
            => keys.Context.SendAsync<bool>(
                $"{RedisCommand.MOVE}{key}{database}", flags.WithDefaultCategory(RedisCommand.MOVE));

        /// <summary>DUMP: the serialised form of a key, or null if it is not there.</summary>
        /// <param name="keys">The key command group.</param>
        /// <param name="key">The key to serialise.</param>
        /// <param name="flags">Command flags.</param>
        /// <remarks>
        /// A pooled lease rather than a <c>byte[]</c>, like every other payload on this surface: the caller
        /// almost always feeds it straight to <c>RESTORE</c> or to a stream, and an array would be a
        /// per-call allocation nothing can reclaim. It must be disposed.
        /// </remarks>
        public static ValueTask<ReadOnlyLease<byte>?> Dump(this in RespKeys keys, RedisKey key, CommandFlags flags = CommandFlags.None)
            => keys.Context.SendAsync<ReadOnlyLease<byte>?>(
                $"{RedisCommand.DUMP}{key}", flags.WithDefaultCategory(RedisCommand.DUMP));

        /// <summary>
        /// The <c>DB n</c> operand of <c>COPY</c> - two arguments, or none at all.
        /// </summary>
        /// <remarks>
        /// A <see cref="IRespArgument"/> rather than a fragment because it is <i>conditional and
        /// multi-token</i>: a fragment carries a fixed blob and an argument count the writer takes on
        /// trust, whereas this writes through the handler's own <c>AppendFormatted</c>, so the count cannot
        /// disagree with what was written. Writing nothing is a legal implementation and is how the absent
        /// case is spelled - which is what keeps <c>Copy</c> one interpolated string rather than four.
        /// </remarks>
        private readonly struct DatabaseOperand(int database) : IRespArgument
        {
            public void WriteTo(scoped ref RespCommandHandler handler)
            {
                if (database < 0) return; // absent: no tokens, no count
                handler.AppendFormatted(RespLiterals.Db);
                handler.AppendFormatted((RedisValue)database);
            }
        }

        /// <remarks>
        /// As the hash group's selector, over the un-prefixed commands. The same rejection applies:
        /// <c>KEEPTTL</c> and <c>PERSIST</c> are not deadlines, and <c>PERSIST</c> is its own command.
        /// </remarks>
        private static RedisCommand SelectKeyExpireCommand(Expiration expiry)
        {
            if (expiry.IsKeepTtl || expiry.IsPersist || !(expiry.IsAbsolute || expiry.IsRelative))
            {
                throw new ArgumentException(
                    "A deadline is required; KEEPTTL and PERSIST are not expirations, and PERSIST is a separate command.",
                    nameof(expiry));
            }

            return expiry.IsAbsolute
                ? (expiry.IsMilliseconds ? RedisCommand.PEXPIREAT : RedisCommand.EXPIREAT)
                : (expiry.IsMilliseconds ? RedisCommand.PEXPIRE : RedisCommand.EXPIRE);
        }
    }
}
