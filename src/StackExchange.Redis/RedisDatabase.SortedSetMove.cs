using System;
using System.Threading.Tasks;

// ReSharper disable once CheckNamespace
namespace StackExchange.Redis;

// Redis 8.12 atomic sorted-set moves: ZMOVE and ZMOVEMEMBER.
// BZMOVE is deliberately absent: a blocking command parks the shared connection (see docs/CommandCoverage.md).
internal partial class RedisDatabase
{
    public SortedSetEntry[]? SortedSetMove(RedisKey sourceKey, RedisKey destinationKey, Order order = Order.Ascending, long? count = null, CommandFlags flags = CommandFlags.None)
    {
        var msg = GetSortedSetMoveMessage(sourceKey, destinationKey, order, count, flags);
        return ExecuteSync(msg, ResultProcessor.NullableSortedSetEntryArray);
    }

    public Task<SortedSetEntry[]?> SortedSetMoveAsync(RedisKey sourceKey, RedisKey destinationKey, Order order = Order.Ascending, long? count = null, CommandFlags flags = CommandFlags.None)
    {
        var msg = GetSortedSetMoveMessage(sourceKey, destinationKey, order, count, flags);
        return ExecuteAsync(msg, ResultProcessor.NullableSortedSetEntryArray);
    }

    public SortedSetEntry[]? SortedSetMoveByScore(RedisKey sourceKey, RedisKey destinationKey, double start, double stop, Exclude exclude = Exclude.None, long? count = null, CommandFlags flags = CommandFlags.None)
    {
        var msg = GetSortedSetMoveByScoreMessage(sourceKey, destinationKey, start, stop, exclude, count, flags);
        return ExecuteSync(msg, ResultProcessor.NullableSortedSetEntryArray);
    }

    public Task<SortedSetEntry[]?> SortedSetMoveByScoreAsync(RedisKey sourceKey, RedisKey destinationKey, double start, double stop, Exclude exclude = Exclude.None, long? count = null, CommandFlags flags = CommandFlags.None)
    {
        var msg = GetSortedSetMoveByScoreMessage(sourceKey, destinationKey, start, stop, exclude, count, flags);
        return ExecuteAsync(msg, ResultProcessor.NullableSortedSetEntryArray);
    }

    public SortedSetEntry[]? SortedSetMoveMembers(RedisKey sourceKey, RedisKey destinationKey, RedisValue[] members, When when = When.Always, CommandFlags flags = CommandFlags.None)
    {
        var msg = GetSortedSetMoveMembersMessage(sourceKey, destinationKey, members, when, flags);
        return ExecuteSync(msg, ResultProcessor.NullableSortedSetEntryArray);
    }

    public Task<SortedSetEntry[]?> SortedSetMoveMembersAsync(RedisKey sourceKey, RedisKey destinationKey, RedisValue[] members, When when = When.Always, CommandFlags flags = CommandFlags.None)
    {
        var msg = GetSortedSetMoveMembersMessage(sourceKey, destinationKey, members, when, flags);
        return ExecuteAsync(msg, ResultProcessor.NullableSortedSetEntryArray);
    }

    // ZMOVE source destination <MIN | MAX> [COUNT count]
    // The count passes through untouched: COUNT is sent if and only if the caller gave one, including values the
    // server rejects (0, negative), so that the server's error is what the caller sees.
    internal Message GetSortedSetMoveMessage(in RedisKey sourceKey, in RedisKey destinationKey, Order order, long? count, CommandFlags flags)
    {
        var end = order switch
        {
            Order.Ascending => RedisLiterals.MIN,
            Order.Descending => RedisLiterals.MAX,
            _ => throw new ArgumentOutOfRangeException(nameof(order)),
        };

        return count is null
            ? Message.Create(Database, flags, RedisCommand.ZMOVE, sourceKey, destinationKey, end)
            : Message.Create(Database, flags, RedisCommand.ZMOVE, sourceKey, destinationKey, end, RedisLiterals.COUNT, count.GetValueOrDefault());
    }

    // ZMOVE source destination BYSCORE score-min score-max [COUNT count]
    // No COUNT means "the whole range", which is a different command to COUNT 1 - so, as above, never defaulted.
    internal Message GetSortedSetMoveByScoreMessage(in RedisKey sourceKey, in RedisKey destinationKey, double start, double stop, Exclude exclude, long? count, CommandFlags flags)
    {
        RedisValue from = GetRange(start, exclude, true), to = GetRange(stop, exclude, false);

        return count is null
            ? Message.Create(Database, flags, RedisCommand.ZMOVE, sourceKey, destinationKey, RedisLiterals.BYSCORE, from, to)
            : Message.Create(Database, flags, RedisCommand.ZMOVE, sourceKey, destinationKey, [RedisLiterals.BYSCORE, from, to, RedisLiterals.COUNT, count.GetValueOrDefault()]);
    }

    // ZMOVEMEMBER source destination [NX | XX] MEMBERS num-members member [member ...]
    // The members go out exactly as given - not de-duplicated or re-ordered - and after MEMBERS <n> the server reads
    // the next n tokens as data, so a member called "NX" or "MEMBERS" is safe. An empty list is sent as MEMBERS 0
    // and left for the server to reject.
    internal Message GetSortedSetMoveMembersMessage(in RedisKey sourceKey, in RedisKey destinationKey, RedisValue[] members, When when, CommandFlags flags)
    {
        if (members is null) throw new ArgumentNullException(nameof(members));

        int offset = when == When.Always ? 2 : 3;
        var args = new RedisValue[offset + members.Length];
        int index = 0;
        switch (when)
        {
            case When.Always:
                break;
            case When.NotExists:
                args[index++] = RedisLiterals.NX;
                break;
            case When.Exists:
                args[index++] = RedisLiterals.XX;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(when));
        }

        args[index++] = RedisLiterals.MEMBERS;
        args[index++] = members.Length;
        members.CopyTo(args, index);

        return Message.Create(Database, flags, RedisCommand.ZMOVEMEMBER, sourceKey, destinationKey, args);
    }
}
