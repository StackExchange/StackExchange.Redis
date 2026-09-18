using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using RESPite;
using RESPite.Messages;
using StackExchange.Redis.Protocol;

namespace StackExchange.Redis;

/// <summary>
/// The sets commands.
/// </summary>
/// <remarks>
/// Here rather than in a single surface-wide class, so that a group is one place. The extension methods
/// bind by namespace, and the namespace is <c>StackExchange.Redis</c>, so this costs a caller nothing.
/// </remarks>
public static partial class Sets
{
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

    /// <summary>
    /// SSCAN, one page at a time: the raw cursor API.
    /// </summary>
    /// <param name="sets">The set command group.</param>
    /// <param name="key">The set to scan.</param>
    /// <param name="cursor">Where to resume; zero starts a new scan.</param>
    /// <param name="pattern">Only return members matching this glob; all of them when omitted.</param>
    /// <param name="pageSize">The <c>COUNT</c> hint; the server's default when omitted.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    /// <returns>A page that must be disposed; see <see cref="RespScanPage{T}"/>.</returns>
    /// <remarks>
    /// <inheritdoc cref="RespScanPage{T}" path="/remarks/para[1]"/>
    /// <para>
    /// The <see cref="RespScanPage{T}.Cursor"/> of the reply is what to pass back here, and <b>only a zero
    /// cursor ends the scan</b> - an empty page does not.
    /// </para>
    /// </remarks>
    public static ValueTask<RespScanPage<RedisValue>> ScanPageAsync(
        this in RespSets sets,
        RedisKey key,
        long cursor = 0,
        RedisValue pattern = default,
        int? pageSize = null,
        CommandFlags flags = CommandFlags.None,
        CancellationToken cancellationToken = default)
    {
        var cmd = ScanCommand(sets.Context, key, cursor, pattern, pageSize);

        // the retry category depends on the cursor: resuming mid-scan is not the same risk as starting one
        return sets.Context.SendAsync(ref cmd, flags.WithScanCursorCategory(cursor), ValueScanHandler, cancellationToken);
    }

    /// <summary>
    /// SSCAN as a sequence, driving the cursor for you.
    /// </summary>
    /// <param name="sets">The set command group.</param>
    /// <param name="key">The set to scan.</param>
    /// <param name="pattern">Only return members matching this glob; all of them when omitted.</param>
    /// <param name="pageSize">The <c>COUNT</c> hint; the server's default when omitted.</param>
    /// <param name="cursor">Where to resume; zero starts a new scan.</param>
    /// <param name="pageOffset">How far into the first page to start, for resuming mid-page.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">
    /// Cancels the scan <b>between pages</b>. Combined with the enumerator's own token when they differ,
    /// and used as-is when they do not - see <c>RespScanEnumerable.GetAsyncEnumerator</c>.
    /// </param>
    /// <remarks>
    /// <b>The utility half</b>, built on
    /// <see cref="ScanPageAsync(in RespSets, RedisKey, long, RedisValue, int?, CommandFlags, CancellationToken)"/>
    /// rather than beside it: it asks for another page when it runs out, so the cursor loop exists once.
    /// The result is also an <see cref="IScanningCursor"/>, so an interrupted scan can report where it had
    /// got to and a later one resume from there.
    /// <para>
    /// <b>Cancellation lands between pages, and that is not a compromise here.</b> The executor refuses a
    /// live token outright - it throws, because the pipeline cannot cancel a request already in flight -
    /// so the token is checked before each fetch and <c>default</c> is passed to the send. For a scan that
    /// is the granularity that matters: what a caller wants to stop is the <i>loop</i>, and a single page
    /// is bounded work. The delegate still carries the token, so when the pipeline can honour one this
    /// becomes a one-word change rather than a redesign.
    /// </para>
    /// </remarks>
    public static IAsyncEnumerable<RedisValue> ScanAsync(
        this in RespSets sets,
        RedisKey key,
        RedisValue pattern = default,
        int? pageSize = null,
        long cursor = 0,
        int pageOffset = 0,
        CommandFlags flags = CommandFlags.None,
        CancellationToken cancellationToken = default)
    {
        // the context is copied into the closure once per enumeration, which is the cost of the convenient
        // shape; the raw API is there for callers who will not pay it
        var context = sets.Context;
        return new RespScanEnumerable<RedisValue>(
            (position, token) =>
            {
                // checked here, and NOT handed to the send: RespExecutor refuses a cancellable token
                // outright today. The enumerator checks it too, before it ever asks for a page.
                token.ThrowIfCancellationRequested();
                return new RespSets(context).ScanPageAsync(key, position, pattern, pageSize, flags);
            },
            cursor,
            pageSize ?? RedisBase.CursorUtils.DefaultRedisPageSize,
            pageOffset,
            cancellationToken);
    }

    /// <summary>Render <c>SSCAN</c> - the one place the command is composed.</summary>
    /// <remarks>
    /// <c>MATCH</c> is omitted for a nil-or-<c>*</c> pattern and <c>COUNT</c> when the caller did not ask,
    /// matching the shipped writer: both are hints, and sending the default explicitly is a wire cost for
    /// nothing.
    /// </remarks>
    private static RespRequestFrame ScanCommand(in RespContext context, RedisKey key, long cursor, RedisValue pattern, int? pageSize)
    {
        if (pageSize is <= 0) throw new ArgumentOutOfRangeException(nameof(pageSize));
        var match = RedisBase.CursorUtils.IsNil(pattern) ? RedisValue.Null : pattern;
        return context.Render(
            $"{RedisCommand.SSCAN}{key}{cursor}{RespLiterals.Match.When(match.HasValue)}{new OptionalValue(match)}{RespLiterals.Count.When(pageSize)}{pageSize}");
    }

    private static readonly RespScanPageHandler<RedisValue> ValueScanHandler
        = new(static (ref RespReader r) => r.ReadRedisValue());
}
