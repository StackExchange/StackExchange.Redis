using System;

namespace StackExchange.Redis;

internal partial class RedisDatabase
{
    private abstract class SortedSetAddMessage(
        int db,
        CommandFlags flags,
        in RedisKey key,
        SortedSetWhen when,
        bool change,
        bool increment) : Message.CommandKeyBase(db, flags, RedisCommand.ZADD, key)
    {
        private const SortedSetWhen KnownWhen =
            SortedSetWhen.Exists | SortedSetWhen.GreaterThan | SortedSetWhen.LessThan | SortedSetWhen.NotExists;

        private readonly SortedSetWhen _when = ValidateWhen(when);
        private readonly bool _change = change;
        private readonly bool _increment = increment;

        public override int ArgCount => 1 + GetOptionCount() + (2 * EntryCount);

        protected abstract int EntryCount { get; }

        protected override void WriteImpl(PhysicalConnection physical)
        {
            physical.WriteHeader(Command, ArgCount);
            physical.Write(Key);
            WriteOptions(physical);
            WriteEntries(physical);
        }

        protected abstract void WriteEntries(PhysicalConnection physical);

        private int GetOptionCount()
        {
            int count = 0;
            if ((_when & SortedSetWhen.NotExists) != 0) count++;
            if ((_when & SortedSetWhen.Exists) != 0) count++;
            if ((_when & SortedSetWhen.GreaterThan) != 0) count++;
            if ((_when & SortedSetWhen.LessThan) != 0) count++;
            if (_change) count++;
            if (_increment) count++;
            return count;
        }

        private static SortedSetWhen ValidateWhen(SortedSetWhen when)
        {
            if ((when & ~KnownWhen) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(when));
            }
            return when;
        }

        private void WriteOptions(PhysicalConnection physical)
        {
            if ((_when & SortedSetWhen.NotExists) != 0)
            {
                physical.WriteBulkString("NX"u8);
            }
            if ((_when & SortedSetWhen.Exists) != 0)
            {
                physical.WriteBulkString("XX"u8);
            }
            if ((_when & SortedSetWhen.GreaterThan) != 0)
            {
                physical.WriteBulkString("GT"u8);
            }
            if ((_when & SortedSetWhen.LessThan) != 0)
            {
                physical.WriteBulkString("LT"u8);
            }
            if (_change)
            {
                physical.WriteBulkString("CH"u8);
            }
            if (_increment)
            {
                physical.WriteBulkString("INCR"u8);
            }
        }
    }

    private sealed class SingleSortedSetAddMessage(
        int db,
        CommandFlags flags,
        in RedisKey key,
        in RedisValue member,
        double score,
        SortedSetWhen when,
        bool change,
        bool increment) : SortedSetAddMessage(db, flags, key, when, change, increment)
    {
        private readonly RedisValue _member = AssertMember(member);
        private readonly double _score = score;

        protected override int EntryCount => 1;

        protected override void WriteEntries(PhysicalConnection physical)
        {
            physical.WriteBulkString(_score);
            physical.WriteBulkString(_member);
        }

        private static RedisValue AssertMember(in RedisValue member)
        {
            member.AssertNotNull();
            return member;
        }
    }

    private sealed class MultipleSortedSetAddMessage(
        int db,
        CommandFlags flags,
        in RedisKey key,
        SortedSetEntry[] values,
        SortedSetWhen when,
        bool change) : SortedSetAddMessage(db, flags, key, when, change, increment: false)
    {
        private readonly SortedSetEntry[] _values = AssertValues(values);

        protected override int EntryCount => _values.Length;

        protected override void WriteEntries(PhysicalConnection physical)
        {
            for (int i = 0; i < _values.Length; i++)
            {
                physical.WriteBulkString(_values[i].score);
                physical.WriteBulkString(_values[i].element);
            }
        }

        private static SortedSetEntry[] AssertValues(SortedSetEntry[] values)
        {
            for (int i = 0; i < values.Length; i++)
            {
                values[i].element.AssertNotNull();
            }
            return values;
        }
    }
}
