using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using RESPite;

namespace StackExchange.Redis.Interpolated
{
    /// <summary>
    /// EXPERIMENTAL SPIKE. The hash-command group: <c>target.Hashes.Get(...)</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The group where the <see cref="Expiration"/> collapse pays for itself most: the old surface spells
    /// the field-lifetime commands as <c>TimeSpan?</c> overloads, <c>DateTime</c> overloads and separate
    /// <c>persist</c>/<c>keepTtl</c> booleans, which is four methods where there is one request. One
    /// <see cref="Expiration"/> parameter says all of it, and picks between <c>HEXPIRE</c>,
    /// <c>HPEXPIRE</c>, <c>HEXPIREAT</c> and <c>HPEXPIREAT</c> on the way past.
    /// </para>
    /// <para>
    /// Two things stay behind. <c>HSCAN</c>/<c>HSCANNOVALUES</c> are deferred-execution cursors, which is
    /// not a frame; and <c>HIMPORT</c> needs its <c>PREPARE</c> injected onto the same physical connection,
    /// which is a property of the write path rather than of the command.
    /// </para>
    /// </remarks>
    [Experimental(Experiments.InterpolatedWriter, UrlFormat = Experiments.UrlFormat)]
    public readonly struct RespHashes
    {
        private readonly RespContext _context;

        /// <summary>Group the hash commands of a context.</summary>
        /// <param name="context">The context to send through.</param>
        public RespHashes(in RespContext context) => _context = context;

        /// <summary>The underlying context.</summary>
        public RespContext Context => _context;
    }

    public static partial class RespSurface
    {
        extension(IRespTarget target)
        {
            /// <summary>The hash commands.</summary>
            public RespHashes Hashes => new(target.Context);
        }

        extension(in RespContext context)
        {
            /// <summary>The hash commands.</summary>
            public RespHashes Hashes => new(context);
        }

        // ---- reads -------------------------------------------------------------------------------------

        /// <summary>HGET.</summary>
        /// <param name="hashes">The hash command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="field">The field to read.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<RedisValue> Get(this in RespHashes hashes, RedisKey key, RedisValue field, CommandFlags flags = CommandFlags.None)
            => hashes.Context.SendAsync<RedisValue>(
                $"{RedisCommand.HGET}{key}{field}", flags.WithDefaultCategory(RedisCommand.HGET));

        /// <summary>HMGET.</summary>
        /// <param name="hashes">The hash command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="fields">The fields to read.</param>
        /// <param name="flags">Command flags.</param>
        /// <remarks>
        /// No fields means no command, as elsewhere: an arity-zero <c>HMGET</c> is a server error, and the
        /// values of no fields is an empty array without asking anyone.
        /// </remarks>
        public static ValueTask<RedisValue[]> Get(this in RespHashes hashes, RedisKey key, ReadOnlySpan<RedisValue> fields, CommandFlags flags = CommandFlags.None)
            => fields.IsEmpty
                ? new ValueTask<RedisValue[]>(Array.Empty<RedisValue>())
                : hashes.Context.SendAsync<RedisValue[]>(
                    $"{RedisCommand.HMGET}{key}{fields}", flags.WithDefaultCategory(RedisCommand.HMGET));

        /// <summary>HGET, retaining the payload as a <see cref="Lease{T}"/> rather than a value.</summary>
        /// <param name="hashes">The hash command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="field">The field to read.</param>
        /// <param name="flags">Command flags.</param>
        /// <remarks>The lease must be disposed.</remarks>
        public static ValueTask<Lease<byte>?> GetLease(this in RespHashes hashes, RedisKey key, RedisValue field, CommandFlags flags = CommandFlags.None)
            => hashes.Context.SendAsync<Lease<byte>?>(
                $"{RedisCommand.HGET}{key}{field}", flags.WithDefaultCategory(RedisCommand.HGET));

        /// <summary>HGETALL.</summary>
        /// <param name="hashes">The hash command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<HashEntry[]> GetAll(this in RespHashes hashes, RedisKey key, CommandFlags flags = CommandFlags.None)
            => hashes.Context.SendAsync<HashEntry[]>(
                $"{RedisCommand.HGETALL}{key}", flags.WithDefaultCategory(RedisCommand.HGETALL));

        /// <summary>HKEYS.</summary>
        /// <param name="hashes">The hash command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<RedisValue[]> Keys(this in RespHashes hashes, RedisKey key, CommandFlags flags = CommandFlags.None)
            => hashes.Context.SendAsync<RedisValue[]>(
                $"{RedisCommand.HKEYS}{key}", flags.WithDefaultCategory(RedisCommand.HKEYS));

        /// <summary>HVALS.</summary>
        /// <param name="hashes">The hash command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<RedisValue[]> Values(this in RespHashes hashes, RedisKey key, CommandFlags flags = CommandFlags.None)
            => hashes.Context.SendAsync<RedisValue[]>(
                $"{RedisCommand.HVALS}{key}", flags.WithDefaultCategory(RedisCommand.HVALS));

        /// <summary>HLEN: how many fields the hash has.</summary>
        /// <param name="hashes">The hash command group.</param>
        /// <param name="key">The key to measure.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<long> Length(this in RespHashes hashes, RedisKey key, CommandFlags flags = CommandFlags.None)
            => hashes.Context.SendAsync<long>(
                $"{RedisCommand.HLEN}{key}", flags.WithDefaultCategory(RedisCommand.HLEN));

        /// <summary>HSTRLEN: how long one field's value is.</summary>
        /// <param name="hashes">The hash command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="field">The field to measure.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<long> StringLength(this in RespHashes hashes, RedisKey key, RedisValue field, CommandFlags flags = CommandFlags.None)
            => hashes.Context.SendAsync<long>(
                $"{RedisCommand.HSTRLEN}{key}{field}", flags.WithDefaultCategory(RedisCommand.HSTRLEN));

        /// <summary>HEXISTS.</summary>
        /// <param name="hashes">The hash command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="field">The field to look for.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<bool> Exists(this in RespHashes hashes, RedisKey key, RedisValue field, CommandFlags flags = CommandFlags.None)
            => hashes.Context.SendAsync<bool>(
                $"{RedisCommand.HEXISTS}{key}{field}", flags.WithDefaultCategory(RedisCommand.HEXISTS));

        /// <summary>HRANDFIELD: one field name, at random.</summary>
        /// <param name="hashes">The hash command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<RedisValue> RandomField(this in RespHashes hashes, RedisKey key, CommandFlags flags = CommandFlags.None)
            => hashes.Context.SendAsync<RedisValue>(
                $"{RedisCommand.HRANDFIELD}{key}", flags.WithDefaultCategory(RedisCommand.HRANDFIELD));

        /// <summary>HRANDFIELD with a count.</summary>
        /// <param name="hashes">The hash command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="count">How many to take; a negative count allows repeats.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<RedisValue[]> RandomFields(this in RespHashes hashes, RedisKey key, long count, CommandFlags flags = CommandFlags.None)
            => hashes.Context.SendAsync<RedisValue[]>(
                $"{RedisCommand.HRANDFIELD}{key}{count}", flags.WithDefaultCategory(RedisCommand.HRANDFIELD));

        /// <summary>HRANDFIELD ... WITHVALUES.</summary>
        /// <param name="hashes">The hash command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="count">How many to take; a negative count allows repeats.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<HashEntry[]> RandomFieldsWithValues(this in RespHashes hashes, RedisKey key, long count, CommandFlags flags = CommandFlags.None)
            => hashes.Context.SendAsync<HashEntry[]>(
                $"{RedisCommand.HRANDFIELD}{key}{count}{RespLiterals.WithValues}",
                flags.WithDefaultCategory(RedisCommand.HRANDFIELD));

        // ---- writes ------------------------------------------------------------------------------------

        /// <summary>HSET, or HSETNX under a condition.</summary>
        /// <param name="hashes">The hash command group.</param>
        /// <param name="key">The key to write.</param>
        /// <param name="field">The field to write.</param>
        /// <param name="value">The value to write.</param>
        /// <param name="when">Whether the field must be absent; default to write unconditionally.</param>
        /// <param name="flags">Command flags.</param>
        /// <remarks>
        /// <para>
        /// <b>HSETNX survives here, where SETNX did not</b>, and the difference is instructive:
        /// <c>SET ... NX</c> exists and replies the same way <c>SET</c> does, so <c>SETNX</c> was a pure
        /// spelling relic. <c>HSET</c> has no NX operand at all - the nearest thing is
        /// <c>HSETEX ... FNX</c>, whose boolean answers a different question (see
        /// <see cref="SetWithExpiry(in RespHashes, RedisKey, RedisValue, RedisValue, Expiration, When, CommandFlags)"/>).
        /// So the two commands stay two commands.
        /// </para>
        /// <para>
        /// A null value deletes the field, as on the old surface and for the same reason it does for
        /// <c>SET</c>: there is no way to store "no value", and an empty string is a different one.
        /// </para>
        /// </remarks>
        public static ValueTask<bool> Set(
            this in RespHashes hashes,
            RedisKey key,
            RedisValue field,
            RedisValue value,
            When when = When.Always,
            CommandFlags flags = CommandFlags.None)
        {
            if (value.IsNull) return Delete(in hashes, key, field, flags);

            var command = when switch
            {
                When.Always => RedisCommand.HSET,
                When.NotExists => RedisCommand.HSETNX,
                _ => ThrowWhen<RedisCommand>(when),
            };

            return hashes.Context.SendAsync<bool>($"{command}{key}{field}{value}", flags.WithDefaultCategory(command));
        }

        /// <summary>HMSET: set several fields in one command.</summary>
        /// <param name="hashes">The hash command group.</param>
        /// <param name="key">The key to write.</param>
        /// <param name="entries">The fields to write.</param>
        /// <param name="flags">Command flags.</param>
        /// <remarks>
        /// Result-less, as on the old surface: <c>HMSET</c> replies <c>+OK</c> and nothing else, so there
        /// is nothing to return - but the reply is still read, because a server error is the only thing
        /// such a call can report.
        /// </remarks>
        public static ValueTask Set(this in RespHashes hashes, RedisKey key, ReadOnlySpan<HashEntry> entries, CommandFlags flags = CommandFlags.None)
            => entries.IsEmpty
                ? default
                : hashes.Context.SendAsync(
                    $"{RedisCommand.HMSET}{key}{entries}", flags.WithDefaultCategory(RedisCommand.HMSET));

        /// <summary>HDEL.</summary>
        /// <param name="hashes">The hash command group.</param>
        /// <param name="key">The key to write.</param>
        /// <param name="field">The field to remove.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<bool> Delete(this in RespHashes hashes, RedisKey key, RedisValue field, CommandFlags flags = CommandFlags.None)
            => hashes.Context.SendAsync<bool>(
                $"{RedisCommand.HDEL}{key}{field}", flags.WithDefaultCategory(RedisCommand.HDEL));

        /// <summary>HDEL with several fields; the reply is how many were removed.</summary>
        /// <param name="hashes">The hash command group.</param>
        /// <param name="key">The key to write.</param>
        /// <param name="fields">The fields to remove.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<long> Delete(this in RespHashes hashes, RedisKey key, ReadOnlySpan<RedisValue> fields, CommandFlags flags = CommandFlags.None)
            => fields.IsEmpty
                ? new ValueTask<long>(0L)
                : hashes.Context.SendAsync<long>(
                    $"{RedisCommand.HDEL}{key}{fields}", flags.WithDefaultCategory(RedisCommand.HDEL));

        /// <summary>HINCRBY, and HINCRBYFLOAT for the floating-point twin.</summary>
        /// <param name="hashes">The hash command group.</param>
        /// <param name="key">The key to write.</param>
        /// <param name="field">The field to increment.</param>
        /// <param name="value">The amount to add.</param>
        /// <param name="flags">Command flags.</param>
        /// <remarks>
        /// There is no Decrement, for the reason there is none on
        /// <see cref="Increment(in RespStrings, RedisKey, long, CommandFlags)"/>: the server has no
        /// HDECRBY, and the old surface's HashDecrement is already a negation.
        /// </remarks>
        public static ValueTask<long> Increment(this in RespHashes hashes, RedisKey key, RedisValue field, long value = 1, CommandFlags flags = CommandFlags.None)
            => hashes.Context.SendAsync<long>(
                $"{RedisCommand.HINCRBY}{key}{field}{value}", flags.WithDefaultCategory(RedisCommand.HINCRBY));

        /// <inheritdoc cref="Increment(in RespHashes, RedisKey, RedisValue, long, CommandFlags)"/>
        /// <param name="hashes">The hash command group.</param>
        /// <param name="key">The key to write.</param>
        /// <param name="field">The field to increment.</param>
        /// <param name="value">The amount to add.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<double> Increment(this in RespHashes hashes, RedisKey key, RedisValue field, double value, CommandFlags flags = CommandFlags.None)
            => hashes.Context.SendAsync<double>(
                $"{RedisCommand.HINCRBYFLOAT}{key}{field}{value}", flags.WithDefaultCategory(RedisCommand.HINCRBYFLOAT));

        // ---- per-field lifetimes -----------------------------------------------------------------------

        /// <summary>HEXPIRE/HPEXPIRE/HEXPIREAT/HPEXPIREAT: give fields a deadline.</summary>
        /// <param name="hashes">The hash command group.</param>
        /// <param name="key">The key to write.</param>
        /// <param name="fields">The fields to expire.</param>
        /// <param name="expiry">When the fields should expire.</param>
        /// <param name="when">The condition the deadline is subject to.</param>
        /// <param name="flags">Command flags.</param>
        /// <remarks>
        /// <para>
        /// <b>Four commands and two overloads become one method.</b> Relative-versus-absolute and
        /// seconds-versus-milliseconds are both already decided by <see cref="Expiration"/> - it normalises
        /// a whole number of seconds to the second form on the way in - and here those two bits pick the
        /// command name rather than an operand token, which is the only thing that makes this group's
        /// expiry different from SET's.
        /// </para>
        /// <para>
        /// <see cref="Expiration.KeepTtl"/> and <see cref="Expiration.Persist"/> have no spelling: the
        /// first is not a deadline and the second is <see cref="Persist"/>, which is a different command
        /// with a different reply. An absent expiry is likewise not a request.
        /// </para>
        /// </remarks>
        public static ValueTask<ExpireResult[]> Expire(
            this in RespHashes hashes,
            RedisKey key,
            ReadOnlySpan<RedisValue> fields,
            Expiration expiry,
            ExpireWhen when = ExpireWhen.Always,
            CommandFlags flags = CommandFlags.None)
        {
            if (fields.IsEmpty) return new ValueTask<ExpireResult[]>(Array.Empty<ExpireResult>());

            var command = SelectExpireCommand(expiry);
            return hashes.Context.SendAsync<ExpireResult[]>(
                $"{command}{key}{expiry.Value}{AsFragment(when)}{RespLiterals.Fields}{fields.Length}{fields}",
                flags.WithRetryCategory(when.AsRetryCategory()).WithDefaultCategory(command));
        }

        /// <summary>HPERSIST: remove the fields' deadlines.</summary>
        /// <param name="hashes">The hash command group.</param>
        /// <param name="key">The key to write.</param>
        /// <param name="fields">The fields to persist.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<PersistResult[]> Persist(this in RespHashes hashes, RedisKey key, ReadOnlySpan<RedisValue> fields, CommandFlags flags = CommandFlags.None)
            => fields.IsEmpty
                ? new ValueTask<PersistResult[]>(Array.Empty<PersistResult>())
                : hashes.Context.SendAsync<PersistResult[]>(
                    $"{RedisCommand.HPERSIST}{key}{RespLiterals.Fields}{fields.Length}{fields}",
                    flags.WithDefaultCategory(RedisCommand.HPERSIST));

        /// <summary>HPTTL: how long the fields have left, in milliseconds.</summary>
        /// <param name="hashes">The hash command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="fields">The fields to ask about.</param>
        /// <param name="flags">Command flags.</param>
        /// <remarks>
        /// Always the millisecond command, as on the old surface: a caller who wanted seconds can divide,
        /// and a caller who needed milliseconds could not recover them.
        /// </remarks>
        public static ValueTask<long[]> GetTimeToLive(this in RespHashes hashes, RedisKey key, ReadOnlySpan<RedisValue> fields, CommandFlags flags = CommandFlags.None)
            => fields.IsEmpty
                ? new ValueTask<long[]>(Array.Empty<long>())
                : hashes.Context.SendAsync<long[]>(
                    $"{RedisCommand.HPTTL}{key}{RespLiterals.Fields}{fields.Length}{fields}",
                    flags.WithDefaultCategory(RedisCommand.HPTTL));

        /// <summary>HPEXPIRETIME: when the fields expire, as a Unix time in milliseconds.</summary>
        /// <param name="hashes">The hash command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="fields">The fields to ask about.</param>
        /// <param name="flags">Command flags.</param>
        /// <remarks><inheritdoc cref="GetTimeToLive" path="/remarks"/></remarks>
        public static ValueTask<long[]> GetExpireDateTime(this in RespHashes hashes, RedisKey key, ReadOnlySpan<RedisValue> fields, CommandFlags flags = CommandFlags.None)
            => fields.IsEmpty
                ? new ValueTask<long[]>(Array.Empty<long>())
                : hashes.Context.SendAsync<long[]>(
                    $"{RedisCommand.HPEXPIRETIME}{key}{RespLiterals.Fields}{fields.Length}{fields}",
                    flags.WithDefaultCategory(RedisCommand.HPEXPIRETIME));

        // ---- read/write combinations -------------------------------------------------------------------

        /// <summary>HGETDEL: read a field and remove it.</summary>
        /// <param name="hashes">The hash command group.</param>
        /// <param name="key">The key to write.</param>
        /// <param name="field">The field to read and remove.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<RedisValue> GetDelete(this in RespHashes hashes, RedisKey key, RedisValue field, CommandFlags flags = CommandFlags.None)
            => hashes.Context.SendAsync(
                $"{RedisCommand.HGETDEL}{key}{RespLiterals.Fields}{1}{field}",
                flags.WithDefaultCategory(RedisCommand.HGETDEL),
                RespHandlers.SingletonValue);

        /// <summary>HGETDEL with several fields.</summary>
        /// <param name="hashes">The hash command group.</param>
        /// <param name="key">The key to write.</param>
        /// <param name="fields">The fields to read and remove.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<RedisValue[]> GetDelete(this in RespHashes hashes, RedisKey key, ReadOnlySpan<RedisValue> fields, CommandFlags flags = CommandFlags.None)
            => fields.IsEmpty
                ? new ValueTask<RedisValue[]>(Array.Empty<RedisValue>())
                : hashes.Context.SendAsync<RedisValue[]>(
                    $"{RedisCommand.HGETDEL}{key}{RespLiterals.Fields}{fields.Length}{fields}",
                    flags.WithDefaultCategory(RedisCommand.HGETDEL));

        /// <summary>HGETDEL, retaining the payload as a <see cref="Lease{T}"/>.</summary>
        /// <param name="hashes">The hash command group.</param>
        /// <param name="key">The key to write.</param>
        /// <param name="field">The field to read and remove.</param>
        /// <param name="flags">Command flags.</param>
        /// <remarks>The lease must be disposed.</remarks>
        public static ValueTask<Lease<byte>?> GetLeaseDelete(this in RespHashes hashes, RedisKey key, RedisValue field, CommandFlags flags = CommandFlags.None)
            => hashes.Context.SendAsync(
                $"{RedisCommand.HGETDEL}{key}{RespLiterals.Fields}{1}{field}",
                flags.WithDefaultCategory(RedisCommand.HGETDEL),
                RespHandlers.SingletonLease);

        /// <summary>HGETEX: read a field, and set, keep or clear its expiration in the same call.</summary>
        /// <param name="hashes">The hash command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="field">The field to read.</param>
        /// <param name="expiry">
        /// The expiration to apply; <see cref="Expiration.Default"/> leaves it untouched, and
        /// <see cref="Expiration.Persist"/> clears it.
        /// </param>
        /// <param name="flags">Command flags.</param>
        /// <remarks><inheritdoc cref="GetSetExpiry(in RespStrings, RedisKey, Expiration, CommandFlags)" path="/remarks/para[2]"/></remarks>
        public static ValueTask<RedisValue> GetSetExpiry(this in RespHashes hashes, RedisKey key, RedisValue field, Expiration expiry = default, CommandFlags flags = CommandFlags.None)
            => hashes.Context.SendAsync(
                $"{RedisCommand.HGETEX}{key}{expiry}{RespLiterals.Fields}{1}{field}",
                WithGetExCategory(expiry, flags),
                RespHandlers.SingletonValue);

        /// <summary>HGETEX with several fields.</summary>
        /// <param name="hashes">The hash command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="fields">The fields to read.</param>
        /// <param name="expiry">The expiration to apply.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<RedisValue[]> GetSetExpiry(this in RespHashes hashes, RedisKey key, ReadOnlySpan<RedisValue> fields, Expiration expiry = default, CommandFlags flags = CommandFlags.None)
            => fields.IsEmpty
                ? new ValueTask<RedisValue[]>(Array.Empty<RedisValue>())
                : hashes.Context.SendAsync<RedisValue[]>(
                    $"{RedisCommand.HGETEX}{key}{expiry}{RespLiterals.Fields}{fields.Length}{fields}",
                    WithGetExCategory(expiry, flags));

        /// <summary>HGETEX, retaining the payload as a <see cref="Lease{T}"/>.</summary>
        /// <param name="hashes">The hash command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="field">The field to read.</param>
        /// <param name="expiry">The expiration to apply.</param>
        /// <param name="flags">Command flags.</param>
        /// <remarks>The lease must be disposed.</remarks>
        public static ValueTask<Lease<byte>?> GetLeaseSetExpiry(this in RespHashes hashes, RedisKey key, RedisValue field, Expiration expiry = default, CommandFlags flags = CommandFlags.None)
            => hashes.Context.SendAsync(
                $"{RedisCommand.HGETEX}{key}{expiry}{RespLiterals.Fields}{1}{field}",
                WithGetExCategory(expiry, flags),
                RespHandlers.SingletonLease);

        /// <summary>HSETEX: write a field and its expiration in one command.</summary>
        /// <param name="hashes">The hash command group.</param>
        /// <param name="key">The key to write.</param>
        /// <param name="field">The field to write.</param>
        /// <param name="value">The value to write.</param>
        /// <param name="expiry">When the field should expire; default for no expiration.</param>
        /// <param name="when">Whether the field must already exist, or must not.</param>
        /// <param name="flags">Command flags.</param>
        /// <remarks>
        /// <para>
        /// <b>Deliberately not folded into <see cref="Set(in RespHashes, RedisKey, RedisValue, RedisValue, When, CommandFlags)"/></b>,
        /// even though it can express everything HSET can. The two booleans answer different questions:
        /// <c>HSET</c> replies with how many fields were <i>new</i>, while <c>HSETEX</c> replies with
        /// whether the write <i>happened</i>. Routing HSET through here would turn "this field was new"
        /// into a constant <see langword="true"/> - a loss of information, not a change of spelling, which
        /// is what separates this from the SETEX and INCR relics that did get folded away.
        /// </para>
        /// <para>
        /// The condition is <c>FNX</c>/<c>FXX</c>, not <c>NX</c>/<c>XX</c>: the key-level tokens mean
        /// something else here, so <see cref="ValueCondition"/> is not the right vocabulary and
        /// <see cref="When"/> is.
        /// </para>
        /// </remarks>
        public static ValueTask<bool> SetWithExpiry(
            this in RespHashes hashes,
            RedisKey key,
            RedisValue field,
            RedisValue value,
            Expiration expiry = default,
            When when = When.Always,
            CommandFlags flags = CommandFlags.None)
        {
            expiry.GetTokenCount(allowEnx: false); // HSETEX has no ENX; say so here rather than on the wire
            return hashes.Context.SendAsync<bool>(
                $"{RedisCommand.HSETEX}{key}{AsFieldCondition(when)}{expiry}{RespLiterals.Fields}{1}{field}{value}",
                flags.WithRetryCategory(when.AsRetryCategory()).WithDefaultCategory(RedisCommand.HSETEX));
        }

        /// <summary>HSETEX with several fields.</summary>
        /// <param name="hashes">The hash command group.</param>
        /// <param name="key">The key to write.</param>
        /// <param name="entries">The fields to write.</param>
        /// <param name="expiry">When the fields should expire; default for no expiration.</param>
        /// <param name="when">Whether the fields must already exist, or must not.</param>
        /// <param name="flags">Command flags.</param>
        /// <remarks><inheritdoc cref="SetWithExpiry(in RespHashes, RedisKey, RedisValue, RedisValue, Expiration, When, CommandFlags)" path="/remarks"/></remarks>
        public static ValueTask<bool> SetWithExpiry(
            this in RespHashes hashes,
            RedisKey key,
            ReadOnlySpan<HashEntry> entries,
            Expiration expiry = default,
            When when = When.Always,
            CommandFlags flags = CommandFlags.None)
        {
            if (entries.IsEmpty) return new ValueTask<bool>(false);

            expiry.GetTokenCount(allowEnx: false);
            return hashes.Context.SendAsync<bool>(
                $"{RedisCommand.HSETEX}{key}{AsFieldCondition(when)}{expiry}{RespLiterals.Fields}{entries.Length}{entries}",
                flags.WithRetryCategory(when.AsRetryCategory()).WithDefaultCategory(RedisCommand.HSETEX));
        }

        // ---- shared -------------------------------------------------------------------------------------

        /// <summary>
        /// Which of the four field-expiry commands an <see cref="Expiration"/> asks for.
        /// </summary>
        /// <remarks>
        /// The mode lives in the command name here, not in an operand - so unlike every other use of
        /// <see cref="Expiration"/> this reads its shape rather than writing its tokens, and only the
        /// numeric <see cref="Expiration.Value"/> goes on the wire.
        /// </remarks>
        private static RedisCommand SelectExpireCommand(Expiration expiry)
        {
            if (expiry.IsKeepTtl || expiry.IsPersist || !(expiry.IsAbsolute || expiry.IsRelative))
            {
                throw new ArgumentException(
                    "A deadline is required; KEEPTTL and PERSIST are not expirations, and PERSIST is a separate command.",
                    nameof(expiry));
            }

            return expiry.IsAbsolute
                ? (expiry.IsMilliseconds ? RedisCommand.HPEXPIREAT : RedisCommand.HEXPIREAT)
                : (expiry.IsMilliseconds ? RedisCommand.HPEXPIRE : RedisCommand.HEXPIRE);
        }

        /// <summary>The NX/XX/GT/LT condition of the field-expiry commands, or nothing.</summary>
        private static RespFragment AsFragment(ExpireWhen when) => when switch
        {
            ExpireWhen.Always => default, // a zero-argument fragment: written, contributes nothing
            ExpireWhen.HasExpiry => RespLiterals.Xx,
            ExpireWhen.HasNoExpiry => RespLiterals.Nx,
            ExpireWhen.GreaterThanCurrentExpiry => RespLiterals.Gt,
            ExpireWhen.LessThanCurrentExpiry => RespLiterals.Lt,
            _ => throw new ArgumentOutOfRangeException(nameof(when)),
        };

        /// <summary>The FNX/FXX condition of <c>HSETEX</c>, or nothing.</summary>
        private static RespFragment AsFieldCondition(When when) => when switch
        {
            When.Always => default,
            When.Exists => RespLiterals.Fxx,
            When.NotExists => RespLiterals.Fnx,
            _ => throw new ArgumentOutOfRangeException(nameof(when)),
        };

        /// <summary>
        /// A bare <c>HGETEX</c> is the pure read the table says it is; any expiry operand mutates the TTL
        /// and makes it a write. The same rule as <c>GETEX</c>, and the same one <c>RedisDatabase</c> applies.
        /// </summary>
        private static CommandFlags WithGetExCategory(Expiration expiry, CommandFlags flags)
        {
            if (expiry.GetTokenCount(allowEnx: false) != 0)
            {
                flags = flags.WithRetryCategory(CommandFlags.CommandRetryWriteLastWins);
            }

            return flags.WithDefaultCategory(RedisCommand.HGETEX);
        }

        /// <summary>Reject a <see cref="When"/> a command has no spelling for.</summary>
        /// <typeparam name="T">The return type of the call site, which never receives a value.</typeparam>
        private static T ThrowWhen<T>(When when)
            => throw new ArgumentOutOfRangeException(nameof(when), when, "This command does not support that condition.");
    }
}
