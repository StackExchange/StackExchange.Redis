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
        private const SortedSetWhen Change = (SortedSetWhen)(1 << 30);
        private const SortedSetWhen Increment = (SortedSetWhen)(1 << 29);

        private readonly SortedSetWhen _when = GetWhen(when, change, increment);

        public override int ArgCount => 1 + GetOptionCount() + (2 * EntryCount);

        protected abstract int EntryCount { get; }

        protected override void WriteImpl(in MessageWriter writer)
        {
            writer.WriteHeader(Command, ArgCount);
            writer.Write(Key);
            WriteOptions(writer);
            WriteEntries(writer);
        }

        protected abstract void WriteEntries(in MessageWriter writer);

        private int GetOptionCount()
        {
            int count = 0;
            if ((_when & SortedSetWhen.NotExists) != 0) count++;
            if ((_when & SortedSetWhen.Exists) != 0) count++;
            if ((_when & SortedSetWhen.GreaterThan) != 0) count++;
            if ((_when & SortedSetWhen.LessThan) != 0) count++;
            if ((_when & Change) != 0) count++;
            if ((_when & Increment) != 0) count++;
            return count;
        }

        private static SortedSetWhen GetWhen(SortedSetWhen when, bool change, bool increment)
        {
            if ((when & ~KnownWhen) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(when));
            }
            if (change) when |= Change;
            if (increment) when |= Increment;
            return when;
        }

        private void WriteOptions(in MessageWriter physical)
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
            if ((_when & Change) != 0)
            {
                physical.WriteBulkString("CH"u8);
            }
            if ((_when & Increment) != 0)
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

        protected override void WriteEntries(in MessageWriter writer)
        {
            writer.WriteBulkString(_score);
            writer.WriteBulkString(_member);
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

        protected override void WriteEntries(in MessageWriter writer)
        {
            for (int i = 0; i < _values.Length; i++)
            {
                writer.WriteBulkString(_values[i].score);
                writer.WriteBulkString(_values[i].element);
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
