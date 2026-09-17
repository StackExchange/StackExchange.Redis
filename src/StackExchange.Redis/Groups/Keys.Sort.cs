using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using RESPite;
using RESPite.Messages;

namespace StackExchange.Redis;

/// <summary>
/// SORT and SORT_RO, which hang off the key group rather than one of their own.
/// </summary>
/// <remarks>
/// <b>A group file without a group type</b>, and the reason the layout is per <i>group</i> rather than
/// per file: <c>SORT</c> takes a key and belongs to <see cref="Keys"/>, so it lives in that partial. It
/// keeps its own file because it is a self-contained family with its own operand plumbing, which is what
/// <c>Keys.Sort.cs</c> says and a shared <c>Keys.Methods.cs</c> would not.
/// </remarks>
public static partial class Keys
{
    /// <summary>
    /// SORT / SORT_RO: order the elements of a list, set or sorted set, optionally by an external
    /// pattern and optionally fetching other keys for each element.
    /// </summary>
    /// <param name="keys">The key command group.</param>
    /// <param name="key">The key to sort.</param>
    /// <param name="skip">How many results to discard from the front.</param>
    /// <param name="take">How many results to return, or -1 for all of them.</param>
    /// <param name="order">Which direction to sort in.</param>
    /// <param name="sortType">Whether to compare the elements as numbers or as text.</param>
    /// <param name="by">An external pattern to sort by, rather than the elements themselves.</param>
    /// <param name="get">Patterns to fetch for each element, in place of the element.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    /// <remarks>
    /// <para>
    /// On the <b>key</b> group rather than a group of its own, and not on the three element groups
    /// either: <c>SORT</c> takes any of them and Redis files it under the generic commands, so putting
    /// it here is one method rather than three identical ones.
    /// </para>
    /// <para>
    /// <c>SORT_RO</c> is the replica-eligible spelling, and 7.0; where the server has it and there is
    /// no destination, it goes out instead. Note that plain <c>SORT</c> is deliberately <b>not</b>
    /// primary-only - it is one of the handful of writable commands left out of that list so a
    /// writable replica can serve it - so the fallback still routes as the caller asked. What the
    /// substitution buys is a command the server itself classifies as a read.
    /// </para>
    /// </remarks>
    public static ValueTask<ReadOnlyLease<RespValue>> SortAsync(
        this in RespKeys keys,
        RedisKey key,
        long skip = 0,
        long take = -1,
        Order order = Order.Ascending,
        SortType sortType = SortType.Numeric,
        RedisValue by = default,
        ReadOnlySpan<RedisValue> get = default,
        CommandFlags flags = CommandFlags.None,
        CancellationToken cancellationToken = default)
        => SortCore<ReadOnlyLease<RespValue>>(in keys, default, key, skip, take, order, sortType, by, get, flags);

    /// <summary>SORT ... STORE: the same sort, written to a key as a list; the reply is its length.</summary>
    /// <param name="keys">The key command group.</param>
    /// <param name="destination">The key to write the sorted result to.</param>
    /// <param name="key">The key to sort.</param>
    /// <param name="skip"><inheritdoc cref="SortAsync" path="/param[@name='skip']"/></param>
    /// <param name="take"><inheritdoc cref="SortAsync" path="/param[@name='take']"/></param>
    /// <param name="order"><inheritdoc cref="SortAsync" path="/param[@name='order']"/></param>
    /// <param name="sortType"><inheritdoc cref="SortAsync" path="/param[@name='sortType']"/></param>
    /// <param name="by"><inheritdoc cref="SortAsync" path="/param[@name='by']"/></param>
    /// <param name="get"><inheritdoc cref="SortAsync" path="/param[@name='get']"/></param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    /// <remarks>
    /// Always <c>SORT</c>, never <c>SORT_RO</c>: a destination makes this a write however read-only
    /// the sort itself is, which is also why the retry category is raised here and nowhere else in
    /// the pair.
    /// </remarks>
    public static ValueTask<long> SortAndStoreAsync(
        this in RespKeys keys,
        RedisKey destination,
        RedisKey key,
        long skip = 0,
        long take = -1,
        Order order = Order.Ascending,
        SortType sortType = SortType.Numeric,
        RedisValue by = default,
        ReadOnlySpan<RedisValue> get = default,
        CommandFlags flags = CommandFlags.None,
        CancellationToken cancellationToken = default)
    {
        if (destination.IsNull) throw new ArgumentNullException(nameof(destination));
        return SortCore<long>(in keys, destination, key, skip, take, order, sortType, by, get, flags);
    }

    /// <summary>
    /// <see cref="SortAsync"/> for the old <see cref="IDatabase"/> shape, which promises an array the caller
    /// owns rather than a lease it has to return.
    /// </summary>
    /// <param name="keys">The key command group.</param>
    /// <param name="key"><inheritdoc cref="SortAsync" path="/param[@name='key']"/></param>
    /// <param name="skip"><inheritdoc cref="SortAsync" path="/param[@name='skip']"/></param>
    /// <param name="take"><inheritdoc cref="SortAsync" path="/param[@name='take']"/></param>
    /// <param name="order"><inheritdoc cref="SortAsync" path="/param[@name='order']"/></param>
    /// <param name="sortType"><inheritdoc cref="SortAsync" path="/param[@name='sortType']"/></param>
    /// <param name="by"><inheritdoc cref="SortAsync" path="/param[@name='by']"/></param>
    /// <param name="get"><inheritdoc cref="SortAsync" path="/param[@name='get']"/></param>
    /// <param name="flags">Command flags.</param>
    internal static ValueTask<RedisValue[]> SortArray(
        this in RespKeys keys,
        RedisKey key,
        long skip,
        long take,
        Order order,
        SortType sortType,
        RedisValue by,
        ReadOnlySpan<RedisValue> get,
        CommandFlags flags)
        => SortCore<RedisValue[]>(in keys, default, key, skip, take, order, sortType, by, get, flags);

    /// <summary>The one renderer; the destination is what makes it a write.</summary>
    private static ValueTask<TResult> SortCore<TResult>(
        in RespKeys keys,
        in RedisKey destination,
        in RedisKey key,
        long skip,
        long take,
        Order order,
        SortType sortType,
        RedisValue by,
        ReadOnlySpan<RedisValue> get,
        CommandFlags flags)
    {
        // validated before anything is written, so a bad argument throws at the caller rather than
        // halfway through a frame
        var descending = order switch
        {
            Order.Ascending => false,
            Order.Descending => true,
            _ => throw new ArgumentOutOfRangeException(nameof(order)),
        };
        var alpha = sortType switch
        {
            SortType.Numeric => false,
            SortType.Alphabetic => true,
            _ => throw new ArgumentOutOfRangeException(nameof(sortType)),
        };

        var context = keys.Context;
        var command = SelectCommand(in context, in destination, in key, ref flags);

        // these defaults mean "everything", and the server already assumes them
        var limited = skip != 0 || take != -1;
        var argHint = 2 + (by.IsNull ? 0 : 2) + (limited ? 3 : 0) + (descending ? 1 : 0)
            + (alpha ? 1 : 0) + (get.Length * 2) + (destination.IsNull ? 0 : 2);

        var cmd = context.Compose(command, argHint);
        try
        {
            cmd.AppendFormatted(key);
            if (!by.IsNull)
            {
                cmd.AppendFormatted(RespLiterals.By);
                cmd.AppendFormatted(by);
            }

            if (limited)
            {
                cmd.AppendFormatted(RespLiterals.Limit);
                cmd.AppendFormatted((RedisValue)skip);
                cmd.AppendFormatted((RedisValue)take);
            }

            if (descending) cmd.AppendFormatted(RespLiterals.Desc);
            if (alpha) cmd.AppendFormatted(RespLiterals.Alpha);

            // GET is written once per pattern, so this is the one operand that cannot be a fragment
            foreach (var pattern in get)
            {
                cmd.AppendFormatted(RespLiterals.Get);
                cmd.AppendFormatted(pattern);
            }

            if (!destination.IsNull)
            {
                cmd.AppendFormatted(RespLiterals.Store);
                cmd.AppendFormatted(destination);
            }
        }
        catch
        {
            cmd.Dispose();
            throw;
        }

        var frame = cmd.Complete();
        return context.SendAsync(ref frame, flags, RespHandlers.Inbuilt<TResult>.Require(), default);
    }

    /// <summary>SORT or SORT_RO, and what that means for retries and routing.</summary>
    private static RedisCommand SelectCommand(in RespContext context, in RedisKey destination, in RedisKey key, ref CommandFlags flags)
    {
        var readOnly = destination.IsNull
            && context.CommandMap.IsAvailable(RedisCommand.SORT_RO)
            && context.TryGetFeatures(RedisCommand.SORT_RO, in key, flags, out var features)
            && features.ReadOnlySort;

        var command = readOnly ? RedisCommand.SORT_RO : RedisCommand.SORT;

        if (!destination.IsNull)
        {
            // SORT is categorised read-only by default, because that is the common case; the STORE
            // variant writes the destination, and a replay of it would otherwise look harmless.
            // BEFORE the default, not after: both of these are first-wins, so the table would
            // already have claimed the slot and this would silently do nothing.
            flags = flags.WithRetryCategory(CommandFlags.CommandRetryWriteLastWins);
        }

        flags = flags.WithDefaultCategory(command);

        // and NOT a routing change: SORT is one of the handful of writable commands deliberately left
        // out of IsPrimaryOnly, so that a writable replica can serve it when the caller asks for one.
        // The old surface only declines to PIN the server it probed, which has no equivalent here.
        return command;
    }
}
