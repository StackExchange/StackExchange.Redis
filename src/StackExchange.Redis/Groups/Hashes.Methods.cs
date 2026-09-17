using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using RESPite;
using RESPite.Messages;
using StackExchange.Redis.Protocol;

namespace StackExchange.Redis;

/// <summary>
/// The hashes commands.
/// </summary>
/// <remarks>
/// Here rather than in a single surface-wide class, so that a group is one place. The extension methods
/// bind by namespace, and the namespace is <c>StackExchange.Redis</c>, so this costs a caller nothing.
/// </remarks>
public static partial class Hashes
{
    // ---- reads -------------------------------------------------------------------------------------

    /// <summary>HGET.</summary>
    /// <param name="hashes">The hash command group.</param>
    /// <param name="key">The key to read.</param>
    /// <param name="field">The field to read.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    public static ValueTask<RedisValue> GetAsync(this in RespHashes hashes, RedisKey key, RedisValue field, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => hashes.Context.SendAsync<RedisValue>(
            $"{RedisCommand.HGET}{key}{field}", flags, cancellationToken: cancellationToken);

    /// <summary>HMGET.</summary>
    /// <param name="hashes">The hash command group.</param>
    /// <param name="key">The key to read.</param>
    /// <param name="fields">The fields to read.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    /// <remarks>
    /// No fields means no command, as elsewhere: an arity-zero <c>HMGET</c> is a server error, and the
    /// values of no fields is an empty array without asking anyone.
    /// </remarks>
    public static ValueTask<ReadOnlyLease<RespValue>> GetAsync(this in RespHashes hashes, RedisKey key, ReadOnlySpan<RedisValue> fields, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => fields.IsEmpty
            ? new ValueTask<ReadOnlyLease<RespValue>>(ReadOnlyLease<RespValue>.Empty)
            : hashes.Context.SendAsync<ReadOnlyLease<RespValue>>(
                $"{RedisCommand.HMGET}{key}{fields}", flags, cancellationToken: cancellationToken);

    /// <summary>Get, as an array, for the old <c>IDatabase</c> surface.</summary>
    /// <remarks>
    /// Internal sibling of <c>Get</c>. A sibling rather than a conversion: <c>IDatabase</c> promises
    /// an array the caller owns, so going via the lease would rent a pooled buffer only to copy out of
    /// it.
    /// <para>
    /// <b>Permanent, not scaffolding.</b> <c>IDatabase</c> is not going anywhere - compatibility
    /// outranks tidiness here - so this is how that signature is served from the new core, for as long
    /// as the signature exists. Internal because the array is the <i>old</i> spelling: new code should
    /// reach for the lease, and nothing outside this assembly should be able to choose otherwise.
    /// </para>
    /// </remarks>
    internal static ValueTask<RedisValue[]> GetArray(this in RespHashes hashes, RedisKey key, ReadOnlySpan<RedisValue> fields, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => fields.IsEmpty
            ? new ValueTask<RedisValue[]>(Array.Empty<RedisValue>())
            : hashes.Context.SendAsync<RedisValue[]>(
                $"{RedisCommand.HMGET}{key}{fields}", flags, cancellationToken: cancellationToken);

    /// <summary>HGET, retaining the payload as a <see cref="Lease{T}"/> rather than a value.</summary>
    /// <param name="hashes">The hash command group.</param>
    /// <param name="key">The key to read.</param>
    /// <param name="field">The field to read.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    /// <remarks>The lease must be disposed.</remarks>
    public static ValueTask<ReadOnlyLease<byte>?> GetLeaseAsync(this in RespHashes hashes, RedisKey key, RedisValue field, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => hashes.Context.SendAsync<ReadOnlyLease<byte>?>(
            $"{RedisCommand.HGET}{key}{field}", flags, cancellationToken: cancellationToken);

    /// <inheritdoc cref="Hashes.GetLeaseAsync(in RespHashes, RedisKey, RedisValue, CommandFlags, CancellationToken)"/>
    /// <param name="hashes">The hash command group.</param>
    /// <param name="key">The key to read.</param>
    /// <param name="field">The field to read.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    /// <remarks>The writable-lease sibling; see <see cref="Strings.GetWritableLease(in RespStrings, RedisKey, CommandFlags, CancellationToken)"/>.</remarks>
    internal static ValueTask<Lease<byte>?> GetWritableLease(this in RespHashes hashes, RedisKey key, RedisValue field, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => hashes.Context.SendAsync<Lease<byte>?>(
            $"{RedisCommand.HGET}{key}{field}", flags, cancellationToken: cancellationToken);

    /// <summary>HGETALL.</summary>
    /// <param name="hashes">The hash command group.</param>
    /// <param name="key">The key to read.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    public static ValueTask<ReadOnlyLease<HashEntry>> GetAllAsync(this in RespHashes hashes, RedisKey key, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => hashes.Context.SendAsync<ReadOnlyLease<HashEntry>>(
            $"{RedisCommand.HGETALL}{key}", flags, cancellationToken: cancellationToken);

    /// <summary>GetAll, as an array, for the old <c>IDatabase</c> surface.</summary>
    /// <remarks>
    /// Internal sibling of <c>GetAll</c>. A sibling rather than a conversion: <c>IDatabase</c> promises
    /// an array the caller owns, so going via the lease would rent a pooled buffer only to copy out of
    /// it.
    /// <para>
    /// <b>Permanent, not scaffolding.</b> <c>IDatabase</c> is not going anywhere - compatibility
    /// outranks tidiness here - so this is how that signature is served from the new core, for as long
    /// as the signature exists. Internal because the array is the <i>old</i> spelling: new code should
    /// reach for the lease, and nothing outside this assembly should be able to choose otherwise.
    /// </para>
    /// </remarks>
    internal static ValueTask<HashEntry[]> GetAllArray(this in RespHashes hashes, RedisKey key, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => hashes.Context.SendAsync<HashEntry[]>(
            $"{RedisCommand.HGETALL}{key}", flags, cancellationToken: cancellationToken);

    /// <summary>HKEYS.</summary>
    /// <param name="hashes">The hash command group.</param>
    /// <param name="key">The key to read.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    public static ValueTask<ReadOnlyLease<RespValue>> KeysAsync(this in RespHashes hashes, RedisKey key, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => hashes.Context.SendAsync<ReadOnlyLease<RespValue>>(
            $"{RedisCommand.HKEYS}{key}", flags, cancellationToken: cancellationToken);

    /// <summary>Keys, as an array, for the old <c>IDatabase</c> surface.</summary>
    /// <remarks>
    /// Internal sibling of <c>Keys</c>. A sibling rather than a conversion: <c>IDatabase</c> promises
    /// an array the caller owns, so going via the lease would rent a pooled buffer only to copy out of
    /// it.
    /// <para>
    /// <b>Permanent, not scaffolding.</b> <c>IDatabase</c> is not going anywhere - compatibility
    /// outranks tidiness here - so this is how that signature is served from the new core, for as long
    /// as the signature exists. Internal because the array is the <i>old</i> spelling: new code should
    /// reach for the lease, and nothing outside this assembly should be able to choose otherwise.
    /// </para>
    /// </remarks>
    internal static ValueTask<RedisValue[]> KeysArray(this in RespHashes hashes, RedisKey key, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => hashes.Context.SendAsync<RedisValue[]>(
            $"{RedisCommand.HKEYS}{key}", flags, cancellationToken: cancellationToken);

    /// <summary>HVALS.</summary>
    /// <param name="hashes">The hash command group.</param>
    /// <param name="key">The key to read.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    public static ValueTask<ReadOnlyLease<RespValue>> ValuesAsync(this in RespHashes hashes, RedisKey key, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => hashes.Context.SendAsync<ReadOnlyLease<RespValue>>(
            $"{RedisCommand.HVALS}{key}", flags, cancellationToken: cancellationToken);

    /// <summary>Values, as an array, for the old <c>IDatabase</c> surface.</summary>
    /// <remarks>
    /// Internal sibling of <c>Values</c>. A sibling rather than a conversion: <c>IDatabase</c> promises
    /// an array the caller owns, so going via the lease would rent a pooled buffer only to copy out of
    /// it.
    /// <para>
    /// <b>Permanent, not scaffolding.</b> <c>IDatabase</c> is not going anywhere - compatibility
    /// outranks tidiness here - so this is how that signature is served from the new core, for as long
    /// as the signature exists. Internal because the array is the <i>old</i> spelling: new code should
    /// reach for the lease, and nothing outside this assembly should be able to choose otherwise.
    /// </para>
    /// </remarks>
    internal static ValueTask<RedisValue[]> ValuesArray(this in RespHashes hashes, RedisKey key, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => hashes.Context.SendAsync<RedisValue[]>(
            $"{RedisCommand.HVALS}{key}", flags, cancellationToken: cancellationToken);

    /// <summary>HLEN: how many fields the hash has.</summary>
    /// <param name="hashes">The hash command group.</param>
    /// <param name="key">The key to measure.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    public static ValueTask<long> LengthAsync(this in RespHashes hashes, RedisKey key, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => hashes.Context.SendAsync<long>(
            $"{RedisCommand.HLEN}{key}", flags, cancellationToken: cancellationToken);

    /// <summary>HSTRLEN: how long one field's value is.</summary>
    /// <param name="hashes">The hash command group.</param>
    /// <param name="key">The key to read.</param>
    /// <param name="field">The field to measure.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    public static ValueTask<long> StringLengthAsync(this in RespHashes hashes, RedisKey key, RedisValue field, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => hashes.Context.SendAsync<long>(
            $"{RedisCommand.HSTRLEN}{key}{field}", flags, cancellationToken: cancellationToken);

    /// <summary>HEXISTS.</summary>
    /// <param name="hashes">The hash command group.</param>
    /// <param name="key">The key to read.</param>
    /// <param name="field">The field to look for.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    public static ValueTask<bool> ExistsAsync(this in RespHashes hashes, RedisKey key, RedisValue field, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => hashes.Context.SendAsync<bool>(
            $"{RedisCommand.HEXISTS}{key}{field}", flags, cancellationToken: cancellationToken);

    /// <summary>HRANDFIELD: one field name, at random.</summary>
    /// <param name="hashes">The hash command group.</param>
    /// <param name="key">The key to read.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    public static ValueTask<RedisValue> RandomFieldAsync(this in RespHashes hashes, RedisKey key, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => hashes.Context.SendAsync<RedisValue>(
            $"{RedisCommand.HRANDFIELD}{key}", flags.NeverCached(), cancellationToken: cancellationToken);

    /// <summary>HRANDFIELD with a count.</summary>
    /// <param name="hashes">The hash command group.</param>
    /// <param name="key">The key to read.</param>
    /// <param name="count">How many to take; a negative count allows repeats.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    public static ValueTask<ReadOnlyLease<RespValue>> RandomFieldsAsync(this in RespHashes hashes, RedisKey key, long count, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => hashes.Context.SendAsync<ReadOnlyLease<RespValue>>(
            $"{RedisCommand.HRANDFIELD}{key}{count}", flags.NeverCached(), cancellationToken: cancellationToken);

    /// <summary>RandomFields, as an array, for the old <c>IDatabase</c> surface.</summary>
    /// <remarks>
    /// Internal sibling of <c>RandomFields</c>. A sibling rather than a conversion: <c>IDatabase</c> promises
    /// an array the caller owns, so going via the lease would rent a pooled buffer only to copy out of
    /// it.
    /// <para>
    /// <b>Permanent, not scaffolding.</b> <c>IDatabase</c> is not going anywhere - compatibility
    /// outranks tidiness here - so this is how that signature is served from the new core, for as long
    /// as the signature exists. Internal because the array is the <i>old</i> spelling: new code should
    /// reach for the lease, and nothing outside this assembly should be able to choose otherwise.
    /// </para>
    /// </remarks>
    internal static ValueTask<RedisValue[]> RandomFieldsArray(this in RespHashes hashes, RedisKey key, long count, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => hashes.Context.SendAsync<RedisValue[]>(
            $"{RedisCommand.HRANDFIELD}{key}{count}", flags.NeverCached(), cancellationToken: cancellationToken);

    /// <summary>HRANDFIELD ... WITHVALUES.</summary>
    /// <param name="hashes">The hash command group.</param>
    /// <param name="key">The key to read.</param>
    /// <param name="count">How many to take; a negative count allows repeats.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    public static ValueTask<ReadOnlyLease<HashEntry>> RandomFieldsWithValuesAsync(this in RespHashes hashes, RedisKey key, long count, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
    {
        var cmd = RandomFieldsWithValuesCommand(hashes.Context, key, count);
        return hashes.Context.SendAsync<ReadOnlyLease<HashEntry>>(ref cmd, flags.NeverCached(), cancellationToken: cancellationToken);
    }

    /// <summary>RandomFieldsWithValues, as an array, for the old <c>IDatabase</c> surface.</summary>
    /// <remarks>
    /// Internal sibling of <c>RandomFieldsWithValues</c>. A sibling rather than a conversion: <c>IDatabase</c> promises
    /// an array the caller owns, so going via the lease would rent a pooled buffer only to copy out of
    /// it.
    /// <para>
    /// <b>Permanent, not scaffolding.</b> <c>IDatabase</c> is not going anywhere - compatibility
    /// outranks tidiness here - so this is how that signature is served from the new core, for as long
    /// as the signature exists. Internal because the array is the <i>old</i> spelling: new code should
    /// reach for the lease, and nothing outside this assembly should be able to choose otherwise.
    /// </para>
    /// </remarks>
    internal static ValueTask<HashEntry[]> RandomFieldsWithValuesArray(this in RespHashes hashes, RedisKey key, long count, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
    {
        var cmd = RandomFieldsWithValuesCommand(hashes.Context, key, count);
        return hashes.Context.SendAsync<HashEntry[]>(ref cmd, flags.NeverCached(), cancellationToken: cancellationToken);
    }

    // ---- writes ------------------------------------------------------------------------------------

    /// <summary>HSET, or HSETNX under a condition.</summary>
    /// <param name="hashes">The hash command group.</param>
    /// <param name="key">The key to write.</param>
    /// <param name="field">The field to write.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="when">Whether the field must be absent; default to write unconditionally.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    /// <remarks>
    /// <para>
    /// <b>HSETNX survives here, where SETNX did not</b>, and the difference is instructive:
    /// <c>SET ... NX</c> exists and replies the same way <c>SET</c> does, so <c>SETNX</c> was a pure
    /// spelling relic. <c>HSET</c> has no NX operand at all - the nearest thing is
    /// <c>HSETEX ... FNX</c>, whose boolean answers a different question (see
    /// <see cref="Hashes.SetWithExpiryAsync(in RespHashes, RedisKey, RedisValue, RedisValue, Expiration, When, CommandFlags, CancellationToken)"/>).
    /// So the two commands stay two commands.
    /// </para>
    /// <para>
    /// A null value deletes the field, as on the old surface and for the same reason it does for
    /// <c>SET</c>: there is no way to store "no value", and an empty string is a different one.
    /// </para>
    /// </remarks>
    public static ValueTask<bool> SetAsync(
        this in RespHashes hashes,
        RedisKey key,
        RedisValue field,
        RedisValue value,
        When when = When.Always,
        CommandFlags flags = CommandFlags.None,
        CancellationToken cancellationToken = default)
    {
        if (value.IsNull) return DeleteAsync(in hashes, key, field, flags);

        var command = when switch
        {
            When.Always => RedisCommand.HSET,
            When.NotExists => RedisCommand.HSETNX,
            _ => ThrowWhen<RedisCommand>(when),
        };

        return hashes.Context.SendAsync<bool>($"{command}{key}{field}{value}", flags, cancellationToken: cancellationToken);
    }

    /// <summary>HMSET: set several fields in one command.</summary>
    /// <param name="hashes">The hash command group.</param>
    /// <param name="key">The key to write.</param>
    /// <param name="entries">The fields to write.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    /// <remarks>
    /// Result-less, as on the old surface: <c>HMSET</c> replies <c>+OK</c> and nothing else, so there
    /// is nothing to return - but the reply is still read, because a server error is the only thing
    /// such a call can report.
    /// </remarks>
    public static ValueTask SetAsync(this in RespHashes hashes, RedisKey key, ReadOnlySpan<HashEntry> entries, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => entries.IsEmpty
            ? default
            : hashes.Context.SendAsync(
                $"{RedisCommand.HMSET}{key}{entries}", flags, cancellationToken: cancellationToken);

    /// <summary>HDEL.</summary>
    /// <param name="hashes">The hash command group.</param>
    /// <param name="key">The key to write.</param>
    /// <param name="field">The field to remove.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    public static ValueTask<bool> DeleteAsync(this in RespHashes hashes, RedisKey key, RedisValue field, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => hashes.Context.SendAsync<bool>(
            $"{RedisCommand.HDEL}{key}{field}", flags, cancellationToken: cancellationToken);

    /// <summary>HDEL with several fields; the reply is how many were removed.</summary>
    /// <param name="hashes">The hash command group.</param>
    /// <param name="key">The key to write.</param>
    /// <param name="fields">The fields to remove.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    public static ValueTask<long> DeleteAsync(this in RespHashes hashes, RedisKey key, ReadOnlySpan<RedisValue> fields, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => fields.IsEmpty
            ? new ValueTask<long>(0L)
            : hashes.Context.SendAsync<long>(
                $"{RedisCommand.HDEL}{key}{fields}", flags, cancellationToken: cancellationToken);

    /// <summary>HINCRBY, and HINCRBYFLOAT for the floating-point twin.</summary>
    /// <param name="hashes">The hash command group.</param>
    /// <param name="key">The key to write.</param>
    /// <param name="field">The field to increment.</param>
    /// <param name="value">The amount to add.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    /// <remarks>
    /// There is no Decrement, for the reason there is none on
    /// <see cref="Strings.IncrementAsync(in RespStrings, RedisKey, long, CommandFlags, CancellationToken)"/>: the server has no
    /// HDECRBY, and the old surface's HashDecrement is already a negation.
    /// </remarks>
    public static ValueTask<long> IncrementAsync(this in RespHashes hashes, RedisKey key, RedisValue field, long value = 1, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => hashes.Context.SendAsync<long>(
            $"{RedisCommand.HINCRBY}{key}{field}{value}", flags, cancellationToken: cancellationToken);

    /// <inheritdoc cref="Hashes.IncrementAsync(in RespHashes, RedisKey, RedisValue, long, CommandFlags, CancellationToken)"/>
    /// <param name="hashes">The hash command group.</param>
    /// <param name="key">The key to write.</param>
    /// <param name="field">The field to increment.</param>
    /// <param name="value">The amount to add.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    public static ValueTask<double> IncrementAsync(this in RespHashes hashes, RedisKey key, RedisValue field, double value, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => hashes.Context.SendAsync<double>(
            $"{RedisCommand.HINCRBYFLOAT}{key}{field}{value}", flags, cancellationToken: cancellationToken);

    // ---- per-field lifetimes -----------------------------------------------------------------------

    /// <summary>HEXPIRE/HPEXPIRE/HEXPIREAT/HPEXPIREAT: give fields a deadline.</summary>
    /// <param name="hashes">The hash command group.</param>
    /// <param name="key">The key to write.</param>
    /// <param name="fields">The fields to expire.</param>
    /// <param name="expiry">When the fields should expire.</param>
    /// <param name="when">The condition the deadline is subject to.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
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
    /// first is not a deadline and the second is <c>Persist</c>, which is a different command
    /// with a different reply. An absent expiry is likewise not a request.
    /// </para>
    /// </remarks>
    public static ValueTask<ReadOnlyLease<ExpireResult>> ExpireAsync(
        this in RespHashes hashes,
        RedisKey key,
        ReadOnlySpan<RedisValue> fields,
        Expiration expiry,
        ExpireWhen when = ExpireWhen.Always,
        CommandFlags flags = CommandFlags.None,
        CancellationToken cancellationToken = default)
    {
        if (fields.IsEmpty) return new ValueTask<ReadOnlyLease<ExpireResult>>(ReadOnlyLease<ExpireResult>.Empty);

        var cmd = ExpireCommand(hashes.Context, key, fields, expiry, when);
        return hashes.Context.SendAsync<ReadOnlyLease<ExpireResult>>(ref cmd, flags.WithRetryCategory(when.AsRetryCategory()), cancellationToken: cancellationToken);
    }

    /// <summary>Expire, as an array, for the old <c>IDatabase</c> surface.</summary>
    /// <remarks>
    /// Internal sibling of <c>Expire</c>. A sibling rather than a conversion: <c>IDatabase</c> promises
    /// an array the caller owns, so going via the lease would rent a pooled buffer only to copy out of
    /// it.
    /// <para>
    /// <b>Permanent, not scaffolding.</b> <c>IDatabase</c> is not going anywhere - compatibility
    /// outranks tidiness here - so this is how that signature is served from the new core, for as long
    /// as the signature exists. Internal because the array is the <i>old</i> spelling: new code should
    /// reach for the lease, and nothing outside this assembly should be able to choose otherwise.
    /// </para>
    /// </remarks>
    internal static ValueTask<ExpireResult[]> ExpireArray(
        this in RespHashes hashes,
        RedisKey key,
        ReadOnlySpan<RedisValue> fields,
        Expiration expiry,
        ExpireWhen when = ExpireWhen.Always,
        CommandFlags flags = CommandFlags.None,
        CancellationToken cancellationToken = default)
    {
        if (fields.IsEmpty) return new ValueTask<ExpireResult[]>(Array.Empty<ExpireResult>());

        var cmd = ExpireCommand(hashes.Context, key, fields, expiry, when);
        return hashes.Context.SendAsync<ExpireResult[]>(ref cmd, flags.WithRetryCategory(when.AsRetryCategory()), cancellationToken: cancellationToken);
    }

    /// <summary>HPERSIST: remove the fields' deadlines.</summary>
    /// <param name="hashes">The hash command group.</param>
    /// <param name="key">The key to write.</param>
    /// <param name="fields">The fields to persist.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    public static ValueTask<ReadOnlyLease<PersistResult>> PersistAsync(this in RespHashes hashes, RedisKey key, ReadOnlySpan<RedisValue> fields, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
    {
        if (fields.IsEmpty) return new ValueTask<ReadOnlyLease<PersistResult>>(ReadOnlyLease<PersistResult>.Empty);

        var cmd = FieldsCommand(hashes.Context, RedisCommand.HPERSIST, key, fields);
        return hashes.Context.SendAsync<ReadOnlyLease<PersistResult>>(ref cmd, flags, cancellationToken: cancellationToken);
    }

    /// <summary>Persist, as an array, for the old <c>IDatabase</c> surface.</summary>
    /// <remarks>
    /// Internal sibling of <c>Persist</c>. A sibling rather than a conversion: <c>IDatabase</c> promises
    /// an array the caller owns, so going via the lease would rent a pooled buffer only to copy out of
    /// it.
    /// <para>
    /// <b>Permanent, not scaffolding.</b> <c>IDatabase</c> is not going anywhere - compatibility
    /// outranks tidiness here - so this is how that signature is served from the new core, for as long
    /// as the signature exists. Internal because the array is the <i>old</i> spelling: new code should
    /// reach for the lease, and nothing outside this assembly should be able to choose otherwise.
    /// </para>
    /// </remarks>
    internal static ValueTask<PersistResult[]> PersistArray(this in RespHashes hashes, RedisKey key, ReadOnlySpan<RedisValue> fields, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
    {
        if (fields.IsEmpty) return new ValueTask<PersistResult[]>(Array.Empty<PersistResult>());

        var cmd = FieldsCommand(hashes.Context, RedisCommand.HPERSIST, key, fields);
        return hashes.Context.SendAsync<PersistResult[]>(ref cmd, flags, cancellationToken: cancellationToken);
    }

    /// <summary>HPTTL: how long the fields have left, in milliseconds.</summary>
    /// <param name="hashes">The hash command group.</param>
    /// <param name="key">The key to read.</param>
    /// <param name="fields">The fields to ask about.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    /// <remarks>
    /// Always the millisecond command, as on the old surface: a caller who wanted seconds can divide,
    /// and a caller who needed milliseconds could not recover them.
    /// </remarks>
    public static ValueTask<ReadOnlyLease<long>> GetTimeToLiveAsync(this in RespHashes hashes, RedisKey key, ReadOnlySpan<RedisValue> fields, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
    {
        if (fields.IsEmpty) return new ValueTask<ReadOnlyLease<long>>(ReadOnlyLease<long>.Empty);

        var cmd = FieldsCommand(hashes.Context, RedisCommand.HPTTL, key, fields);
        return hashes.Context.SendAsync<ReadOnlyLease<long>>(ref cmd, flags.NeverCached(), cancellationToken: cancellationToken);
    }

    /// <summary>GetTimeToLive, as an array, for the old <c>IDatabase</c> surface.</summary>
    /// <remarks>
    /// Internal sibling of <c>GetTimeToLive</c>. A sibling rather than a conversion: <c>IDatabase</c> promises
    /// an array the caller owns, so going via the lease would rent a pooled buffer only to copy out of
    /// it.
    /// <para>
    /// <b>Permanent, not scaffolding.</b> <c>IDatabase</c> is not going anywhere - compatibility
    /// outranks tidiness here - so this is how that signature is served from the new core, for as long
    /// as the signature exists. Internal because the array is the <i>old</i> spelling: new code should
    /// reach for the lease, and nothing outside this assembly should be able to choose otherwise.
    /// </para>
    /// </remarks>
    internal static ValueTask<long[]> GetTimeToLiveArray(this in RespHashes hashes, RedisKey key, ReadOnlySpan<RedisValue> fields, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
    {
        if (fields.IsEmpty) return new ValueTask<long[]>(Array.Empty<long>());

        var cmd = FieldsCommand(hashes.Context, RedisCommand.HPTTL, key, fields);
        return hashes.Context.SendAsync<long[]>(ref cmd, flags.NeverCached(), cancellationToken: cancellationToken);
    }

    /// <summary>HPEXPIRETIME: when the fields expire, as a Unix time in milliseconds.</summary>
    /// <param name="hashes">The hash command group.</param>
    /// <param name="key">The key to read.</param>
    /// <param name="fields">The fields to ask about.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    /// <remarks>
    /// <inheritdoc cref="GetTimeToLiveAsync" path="/remarks"/>
    /// <para>
    /// <b>Cacheable, where <c>GetTimeToLive</c> is not</b>, and the difference is absolute versus
    /// relative rather than a slip. <c>HPTTL</c> counts down: the answer is different a millisecond
    /// later, so it is stale the instant it is stored and nothing will ever say so, because expiry is
    /// announced to nobody. This returns a fixed instant, which does not drift - it only becomes wrong
    /// once the field actually expires, which is the same exposure every cached read of a volatile key
    /// already has, and is what <see cref="CachePolicy.TimeToLive"/> is there to bound.
    /// </para>
    /// <para>
    /// Recorded as a judgement rather than an obvious call: it is on the queue for a second opinion,
    /// next to <c>DUMP</c>.
    /// </para>
    /// </remarks>
    public static ValueTask<ReadOnlyLease<long>> GetExpireDateTimeAsync(this in RespHashes hashes, RedisKey key, ReadOnlySpan<RedisValue> fields, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
    {
        if (fields.IsEmpty) return new ValueTask<ReadOnlyLease<long>>(ReadOnlyLease<long>.Empty);

        var cmd = FieldsCommand(hashes.Context, RedisCommand.HPEXPIRETIME, key, fields);
        return hashes.Context.SendAsync<ReadOnlyLease<long>>(ref cmd, flags, cancellationToken: cancellationToken);
    }

    /// <summary>GetExpireDateTime, as an array, for the old <c>IDatabase</c> surface.</summary>
    /// <remarks>
    /// Internal sibling of <c>GetExpireDateTime</c>. A sibling rather than a conversion: <c>IDatabase</c> promises
    /// an array the caller owns, so going via the lease would rent a pooled buffer only to copy out of
    /// it.
    /// <para>
    /// <b>Permanent, not scaffolding.</b> <c>IDatabase</c> is not going anywhere - compatibility
    /// outranks tidiness here - so this is how that signature is served from the new core, for as long
    /// as the signature exists. Internal because the array is the <i>old</i> spelling: new code should
    /// reach for the lease, and nothing outside this assembly should be able to choose otherwise.
    /// </para>
    /// </remarks>
    internal static ValueTask<long[]> GetExpireDateTimeArray(this in RespHashes hashes, RedisKey key, ReadOnlySpan<RedisValue> fields, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
    {
        if (fields.IsEmpty) return new ValueTask<long[]>(Array.Empty<long>());

        var cmd = FieldsCommand(hashes.Context, RedisCommand.HPEXPIRETIME, key, fields);
        return hashes.Context.SendAsync<long[]>(ref cmd, flags, cancellationToken: cancellationToken);
    }

    // ---- read/write combinations -------------------------------------------------------------------

    /// <summary>HGETDEL: read a field and remove it.</summary>
    /// <param name="hashes">The hash command group.</param>
    /// <param name="key">The key to write.</param>
    /// <param name="field">The field to read and remove.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    public static ValueTask<RedisValue> GetDeleteAsync(this in RespHashes hashes, RedisKey key, RedisValue field, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
    {
        var cmd = FieldCommand(hashes.Context, RedisCommand.HGETDEL, key, field);
        return hashes.Context.SendAsync(ref cmd, flags, RespHandlers.SingletonValue, cancellationToken: cancellationToken);
    }

    /// <summary>HGETDEL with several fields.</summary>
    /// <param name="hashes">The hash command group.</param>
    /// <param name="key">The key to write.</param>
    /// <param name="fields">The fields to read and remove.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    public static ValueTask<ReadOnlyLease<RespValue>> GetDeleteAsync(this in RespHashes hashes, RedisKey key, ReadOnlySpan<RedisValue> fields, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
    {
        if (fields.IsEmpty) return new ValueTask<ReadOnlyLease<RespValue>>(ReadOnlyLease<RespValue>.Empty);

        var cmd = FieldsCommand(hashes.Context, RedisCommand.HGETDEL, key, fields);
        return hashes.Context.SendAsync<ReadOnlyLease<RespValue>>(ref cmd, flags, cancellationToken: cancellationToken);
    }

    /// <summary>GetDelete, as an array, for the old <c>IDatabase</c> surface.</summary>
    /// <remarks>
    /// Internal sibling of <c>GetDelete</c>. A sibling rather than a conversion: <c>IDatabase</c> promises
    /// an array the caller owns, so going via the lease would rent a pooled buffer only to copy out of
    /// it.
    /// <para>
    /// <b>Permanent, not scaffolding.</b> <c>IDatabase</c> is not going anywhere - compatibility
    /// outranks tidiness here - so this is how that signature is served from the new core, for as long
    /// as the signature exists. Internal because the array is the <i>old</i> spelling: new code should
    /// reach for the lease, and nothing outside this assembly should be able to choose otherwise.
    /// </para>
    /// </remarks>
    internal static ValueTask<RedisValue[]> GetDeleteArray(this in RespHashes hashes, RedisKey key, ReadOnlySpan<RedisValue> fields, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
    {
        if (fields.IsEmpty) return new ValueTask<RedisValue[]>(Array.Empty<RedisValue>());

        var cmd = FieldsCommand(hashes.Context, RedisCommand.HGETDEL, key, fields);
        return hashes.Context.SendAsync<RedisValue[]>(ref cmd, flags, cancellationToken: cancellationToken);
    }

    /// <summary>HGETDEL, retaining the payload as a <see cref="Lease{T}"/>.</summary>
    /// <param name="hashes">The hash command group.</param>
    /// <param name="key">The key to write.</param>
    /// <param name="field">The field to read and remove.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    /// <remarks>The lease must be disposed.</remarks>
    public static ValueTask<ReadOnlyLease<byte>?> GetLeaseDeleteAsync(this in RespHashes hashes, RedisKey key, RedisValue field, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
    {
        var cmd = FieldCommand(hashes.Context, RedisCommand.HGETDEL, key, field);
        return hashes.Context.SendAsync(ref cmd, flags, RespHandlers.SingletonReadOnlyLease, cancellationToken: cancellationToken);
    }

    /// <inheritdoc cref="Hashes.GetLeaseDeleteAsync(in RespHashes, RedisKey, RedisValue, CommandFlags, CancellationToken)"/>
    /// <param name="hashes">The hash command group.</param>
    /// <param name="key">The key to write.</param>
    /// <param name="field">The field to read and remove.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    /// <remarks>The writable-lease sibling; see <see cref="Strings.GetWritableLease(in RespStrings, RedisKey, CommandFlags, CancellationToken)"/>.</remarks>
    internal static ValueTask<Lease<byte>?> GetWritableLeaseDelete(this in RespHashes hashes, RedisKey key, RedisValue field, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
    {
        var cmd = FieldCommand(hashes.Context, RedisCommand.HGETDEL, key, field);
        return hashes.Context.SendAsync(ref cmd, flags, RespHandlers.SingletonLease, cancellationToken: cancellationToken);
    }

    /// <summary>HGETEX: read a field, and set, keep or clear its expiration in the same call.</summary>
    /// <param name="hashes">The hash command group.</param>
    /// <param name="key">The key to read.</param>
    /// <param name="field">The field to read.</param>
    /// <param name="expiry">
    /// The expiration to apply; <see cref="Expiration.Default"/> leaves it untouched, and
    /// <see cref="Expiration.Persist"/> clears it.
    /// </param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    /// <remarks><inheritdoc cref="Strings.GetSetExpiryAsync(in RespStrings, RedisKey, Expiration, CommandFlags, CancellationToken)" path="/remarks/para[2]"/></remarks>
    public static ValueTask<RedisValue> GetSetExpiryAsync(this in RespHashes hashes, RedisKey key, RedisValue field, Expiration expiry = default, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
    {
        var cmd = GetSetExpiryCommand(hashes.Context, key, expiry, field);
        return hashes.Context.SendAsync(ref cmd, WithGetExCategory(expiry, flags), RespHandlers.SingletonValue, cancellationToken: cancellationToken);
    }

    /// <summary>HGETEX with several fields.</summary>
    /// <param name="hashes">The hash command group.</param>
    /// <param name="key">The key to read.</param>
    /// <param name="fields">The fields to read.</param>
    /// <param name="expiry">The expiration to apply.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    public static ValueTask<ReadOnlyLease<RespValue>> GetSetExpiryAsync(this in RespHashes hashes, RedisKey key, ReadOnlySpan<RedisValue> fields, Expiration expiry = default, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
    {
        if (fields.IsEmpty) return new ValueTask<ReadOnlyLease<RespValue>>(ReadOnlyLease<RespValue>.Empty);

        var cmd = GetSetExpiryCommand(hashes.Context, key, expiry, fields);
        return hashes.Context.SendAsync<ReadOnlyLease<RespValue>>(ref cmd, WithGetExCategory(expiry, flags), cancellationToken: cancellationToken);
    }

    /// <summary>GetSetExpiry, as an array, for the old <c>IDatabase</c> surface.</summary>
    /// <remarks>
    /// Internal sibling of <c>GetSetExpiry</c>. A sibling rather than a conversion: <c>IDatabase</c> promises
    /// an array the caller owns, so going via the lease would rent a pooled buffer only to copy out of
    /// it.
    /// <para>
    /// <b>Permanent, not scaffolding.</b> <c>IDatabase</c> is not going anywhere - compatibility
    /// outranks tidiness here - so this is how that signature is served from the new core, for as long
    /// as the signature exists. Internal because the array is the <i>old</i> spelling: new code should
    /// reach for the lease, and nothing outside this assembly should be able to choose otherwise.
    /// </para>
    /// </remarks>
    internal static ValueTask<RedisValue[]> GetSetExpiryArray(this in RespHashes hashes, RedisKey key, ReadOnlySpan<RedisValue> fields, Expiration expiry = default, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
    {
        if (fields.IsEmpty) return new ValueTask<RedisValue[]>(Array.Empty<RedisValue>());

        var cmd = GetSetExpiryCommand(hashes.Context, key, expiry, fields);
        return hashes.Context.SendAsync<RedisValue[]>(ref cmd, WithGetExCategory(expiry, flags), cancellationToken: cancellationToken);
    }

    /// <summary>HGETEX, retaining the payload as a <see cref="Lease{T}"/>.</summary>
    /// <param name="hashes">The hash command group.</param>
    /// <param name="key">The key to read.</param>
    /// <param name="field">The field to read.</param>
    /// <param name="expiry">The expiration to apply.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    /// <remarks>The lease must be disposed.</remarks>
    public static ValueTask<ReadOnlyLease<byte>?> GetLeaseSetExpiryAsync(this in RespHashes hashes, RedisKey key, RedisValue field, Expiration expiry = default, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
    {
        var cmd = GetSetExpiryCommand(hashes.Context, key, expiry, field);
        return hashes.Context.SendAsync(ref cmd, WithGetExCategory(expiry, flags), RespHandlers.SingletonReadOnlyLease, cancellationToken: cancellationToken);
    }

    /// <inheritdoc cref="Hashes.GetLeaseSetExpiryAsync(in RespHashes, RedisKey, RedisValue, Expiration, CommandFlags, CancellationToken)"/>
    /// <param name="hashes">The hash command group.</param>
    /// <param name="key">The key to read.</param>
    /// <param name="field">The field to read.</param>
    /// <param name="expiry">The expiration to apply.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    /// <remarks>The writable-lease sibling; see <see cref="Strings.GetWritableLease(in RespStrings, RedisKey, CommandFlags, CancellationToken)"/>.</remarks>
    internal static ValueTask<Lease<byte>?> GetWritableLeaseSetExpiry(this in RespHashes hashes, RedisKey key, RedisValue field, Expiration expiry = default, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
    {
        var cmd = GetSetExpiryCommand(hashes.Context, key, expiry, field);
        return hashes.Context.SendAsync(ref cmd, WithGetExCategory(expiry, flags), RespHandlers.SingletonLease, cancellationToken: cancellationToken);
    }

    /// <summary>HSETEX: write a field and its expiration in one command.</summary>
    /// <param name="hashes">The hash command group.</param>
    /// <param name="key">The key to write.</param>
    /// <param name="field">The field to write.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="expiry">When the field should expire; default for no expiration.</param>
    /// <param name="when">Whether the field must already exist, or must not.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    /// <remarks>
    /// <para>
    /// <b>Deliberately not folded into <see cref="Hashes.SetAsync(in RespHashes, RedisKey, RedisValue, RedisValue, When, CommandFlags, CancellationToken)"/></b>,
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
    public static ValueTask<bool> SetWithExpiryAsync(
        this in RespHashes hashes,
        RedisKey key,
        RedisValue field,
        RedisValue value,
        Expiration expiry = default,
        When when = When.Always,
        CommandFlags flags = CommandFlags.None,
        CancellationToken cancellationToken = default)
    {
        expiry.GetTokenCount(allowEnx: false); // HSETEX has no ENX; say so here rather than on the wire
        return hashes.Context.SendAsync<bool>(
            $"{RedisCommand.HSETEX}{key}{AsFieldCondition(when)}{expiry}{RespLiterals.Fields}{1}{field}{value}",
            flags.WithRetryCategory(when.AsRetryCategory()),
            cancellationToken: cancellationToken);
    }

    /// <summary>HSETEX with several fields.</summary>
    /// <param name="hashes">The hash command group.</param>
    /// <param name="key">The key to write.</param>
    /// <param name="entries">The fields to write.</param>
    /// <param name="expiry">When the fields should expire; default for no expiration.</param>
    /// <param name="when">Whether the fields must already exist, or must not.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    /// <remarks><inheritdoc cref="Hashes.SetWithExpiryAsync(in RespHashes, RedisKey, RedisValue, RedisValue, Expiration, When, CommandFlags, CancellationToken)" path="/remarks"/></remarks>
    public static ValueTask<bool> SetWithExpiryAsync(
        this in RespHashes hashes,
        RedisKey key,
        ReadOnlySpan<HashEntry> entries,
        Expiration expiry = default,
        When when = When.Always,
        CommandFlags flags = CommandFlags.None,
        CancellationToken cancellationToken = default)
    {
        if (entries.IsEmpty) return new ValueTask<bool>(false);

        expiry.GetTokenCount(allowEnx: false);
        return hashes.Context.SendAsync<bool>(
            $"{RedisCommand.HSETEX}{key}{AsFieldCondition(when)}{expiry}{RespLiterals.Fields}{entries.Length}{entries}",
            flags.WithRetryCategory(when.AsRetryCategory()),
            cancellationToken: cancellationToken);
    }

    // ---- shared -------------------------------------------------------------------------------------

    /// <summary>Render <c>HRANDFIELD ... WITHVALUES</c>.</summary>
    /// <remarks>
    /// The trailer is what makes the reply field/value pairs rather than field names, so the lease form
    /// and the array form must not be able to disagree about it.
    /// </remarks>
    private static RespRequestFrame RandomFieldsWithValuesCommand(in RespContext context, RedisKey key, long count)
        => context.Render($"{RedisCommand.HRANDFIELD}{key}{count}{RespLiterals.WithValues}");

    /// <summary>
    /// Render a command that ends <c>FIELDS numfields field [field ...]</c> - the shape this group's
    /// commands share.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Named for the shape, not for a command</b>, because the shape is what is duplicated: eight of
    /// this group's methods write the same trailer, each with a lease form and an array form behind it,
    /// which was the same text eighteen times. The count is <i>derived</i> from the span rather than
    /// passed, so the one way to send a count that disagrees with the fields is to write the trailer
    /// again - and after this there is nowhere to write it.
    /// </para>
    /// <para>
    /// The returned frame owns a pooled buffer and is consumed by the send, so a caller must send it.
    /// An empty span is the caller's problem: the server rejects <c>numfields 0</c>, and every caller
    /// here answers an empty request without sending.
    /// </para>
    /// </remarks>
    private static RespRequestFrame FieldsCommand(in RespContext context, RedisCommand command, RedisKey key, ReadOnlySpan<RedisValue> fields)
        => context.Render($"{command}{key}{RespLiterals.Fields}{fields.Length}{fields}");

    /// <summary>The single-field spelling of <see cref="FieldsCommand"/>; the count is the literal 1.</summary>
    /// <remarks>
    /// Not <c>FieldsCommand(context, command, key, new[] { field })</c>: that would allocate to say
    /// "one", and the count is known at the call rather than counted from anything.
    /// </remarks>
    private static RespRequestFrame FieldCommand(in RespContext context, RedisCommand command, RedisKey key, RedisValue field)
        => context.Render($"{command}{key}{RespLiterals.Fields}{1}{field}");

    /// <summary>Render <c>HEXPIRE</c>/<c>HPEXPIRE</c>/<c>HEXPIREAT</c>/<c>HPEXPIREAT</c>.</summary>
    /// <remarks>
    /// Which of the four it is comes from <see cref="SelectExpireCommand"/>, which means the choice and
    /// the operand that goes with it - <see cref="Expiration.Value"/>, in whichever unit the command
    /// implies - are decided in one place rather than at each of the two call sites.
    /// </remarks>
    private static RespRequestFrame ExpireCommand(in RespContext context, RedisKey key, ReadOnlySpan<RedisValue> fields, Expiration expiry, ExpireWhen when)
        => context.Render($"{SelectExpireCommand(expiry)}{key}{expiry.Value}{RespSurface.AsFragment(when)}{RespLiterals.Fields}{fields.Length}{fields}");

    /// <summary>Render <c>HGETEX</c>, whose expiry comes before the <c>FIELDS</c> trailer.</summary>
    /// <remarks>
    /// Its own factory rather than an argument to <see cref="FieldsCommand"/>, because the expiry is not
    /// part of that shape: it sits between the key and the trailer, and only this command has it.
    /// </remarks>
    private static RespRequestFrame GetSetExpiryCommand(in RespContext context, RedisKey key, Expiration expiry, ReadOnlySpan<RedisValue> fields)
        => context.Render($"{RedisCommand.HGETEX}{key}{expiry}{RespLiterals.Fields}{fields.Length}{fields}");

    /// <summary>The single-field spelling of <see cref="GetSetExpiryCommand(in RespContext, RedisKey, Expiration, ReadOnlySpan{RedisValue})"/>.</summary>
    private static RespRequestFrame GetSetExpiryCommand(in RespContext context, RedisKey key, Expiration expiry, RedisValue field)
        => context.Render($"{RedisCommand.HGETEX}{key}{expiry}{RespLiterals.Fields}{1}{field}");

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

        return flags;
    }

    /// <summary>Reject a <see cref="When"/> a command has no spelling for.</summary>
    /// <typeparam name="T">The return type of the call site, which never receives a value.</typeparam>
    private static T ThrowWhen<T>(When when)
        => throw new ArgumentOutOfRangeException(nameof(when), when, "This command does not support that condition.");
}
