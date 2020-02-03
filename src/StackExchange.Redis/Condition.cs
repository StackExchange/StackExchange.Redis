using System;
using System.Collections.Generic;

#pragma warning disable RCS1231

namespace StackExchange.Redis
{
    /// <summary>
    /// Describes a pre-condition used in a redis transaction
    /// </summary>
    public abstract class Condition
    {
        internal abstract Condition MapKeys(Func<RedisKey, RedisKey> map);

        private Condition() { }

        /// <summary>
        /// Enforces that the given hash-field must have the specified value.
        /// </summary>
        /// <param name="key">The key of the hash to check.</param>
        /// <param name="hashField">The field in the hash to check.</param>
        /// <param name="value">The value that the hash field must match.</param>
        public static Condition HashEqual(RedisKey key, RedisValue hashField, RedisValue value)
        {
            if (hashField.IsNull) throw new ArgumentNullException(nameof(hashField));
            if (value.IsNull) return HashNotExists(key, hashField);
            return new EqualsCondition(key, RedisType.Hash, hashField, true, value);
        }

        /// <summary>
        /// Enforces that the given hash-field must exist.
        /// </summary>
        /// <param name="key">The key of the hash to check.</param>
        /// <param name="hashField">The field in the hash to check.</param>
        public static Condition HashExists(RedisKey key, RedisValue hashField)
        {
            if (hashField.IsNull) throw new ArgumentNullException(nameof(hashField));
            return new ExistsCondition(key, RedisType.Hash, hashField, true);
        }

        /// <summary>
        /// Enforces that the given hash-field must not have the specified value.
        /// </summary>
        /// <param name="key">The key of the hash to check.</param>
        /// <param name="hashField">The field in the hash to check.</param>
        /// <param name="value">The value that the hash field must not match.</param>
        public static Condition HashNotEqual(RedisKey key, RedisValue hashField, RedisValue value)
        {
            if (hashField.IsNull) throw new ArgumentNullException(nameof(hashField));
            if (value.IsNull) return HashExists(key, hashField);
            return new EqualsCondition(key, RedisType.Hash, hashField, false, value);
        }

        /// <summary>
        /// Enforces that the given hash-field must not exist.
        /// </summary>
        /// <param name="key">The key of the hash to check.</param>
        /// <param name="hashField">The field in the hash that must not exist.</param>
        public static Condition HashNotExists(RedisKey key, RedisValue hashField)
        {
            if (hashField.IsNull) throw new ArgumentNullException(nameof(hashField));
            return new ExistsCondition(key, RedisType.Hash, hashField, false);
        }

        /// <summary>
        /// Enforces that the given key must exist.
        /// </summary>
        /// <param name="key">The key that must exist.</param>
        public static Condition KeyExists(RedisKey key) => new ExistsCondition(key, RedisType.None, RedisValue.Null, true);

        /// <summary>
        /// Enforces that the given key must not exist
        /// </summary>
        /// <param name="key">The key that must not exist.</param>
        public static Condition KeyNotExists(RedisKey key) => new ExistsCondition(key, RedisType.None, RedisValue.Null, false);

        /// <summary>
        /// Enforces that the given list index must have the specified value 
        /// </summary>
        /// <param name="key">The key of the list to check.</param>
        /// <param name="index">The position in the list to check.</param>
        /// <param name="value">The value of the list position that must match.</param>
        public static Condition ListIndexEqual(RedisKey key, long index, RedisValue value) => new ListCondition(key, index, true, value);

        /// <summary>
        /// Enforces that the given list index must exist
        /// </summary>
        /// <param name="key">The key of the list to check.</param>
        /// <param name="index">The position in the list that must exist.</param>
        public static Condition ListIndexExists(RedisKey key, long index) => new ListCondition(key, index, true, null);

        /// <summary>
        /// Enforces that the given list index must not have the specified value 
        /// </summary>
        /// <param name="key">The key of the list to check.</param>
        /// <param name="index">The position in the list to check.</param>
        /// <param name="value">The value of the list position must not match.</param>
        public static Condition ListIndexNotEqual(RedisKey key, long index, RedisValue value) => new ListCondition(key, index, false, value);

        /// <summary>
        /// Enforces that the given list index must not exist
        /// </summary>
        /// <param name="key">The key of the list to check.</param>
        /// <param name="index">The position in the list that must not exist.</param>
        public static Condition ListIndexNotExists(RedisKey key, long index) => new ListCondition(key, index, false, null);

        /// <summary>
        /// Enforces that the given key must have the specified value
        /// </summary>
        /// <param name="key">The key to check.</param>
        /// <param name="value">The value that must match.</param>
        public static Condition StringEqual(RedisKey key, RedisValue value)
        {
            if (value.IsNull) return KeyNotExists(key);
            return new EqualsCondition(key, RedisType.Hash, RedisValue.Null, true, value);
        }

        /// <summary>
        /// Enforces that the given key must not have the specified value
        /// </summary>
        /// <param name="key">The key to check.</param>
        /// <param name="value">The value that must not match.</param>
        public static Condition StringNotEqual(RedisKey key, RedisValue value)
        {
            if (value.IsNull) return KeyExists(key);
            return new EqualsCondition(key, RedisType.Hash, RedisValue.Null, false, value);
        }

        /// <summary>
        /// Enforces that the given hash length is a certain value
        /// </summary>
        /// <param name="key">The key of the hash to check.</param>
        /// <param name="length">The length the hash must have.</param>
        public static Condition HashLengthEqual(RedisKey key, long length) => new LengthCondition(key, RedisType.Hash, 0, length);

        /// <summary>
        /// Enforces that the given hash length is less than a certain value
        /// </summary>
        /// <param name="key">The key of the hash to check.</param>
        /// <param name="length">The length the hash must be less than.</param>
        public static Condition HashLengthLessThan(RedisKey key, long length) => new LengthCondition(key, RedisType.Hash, 1, length);

        /// <summary>
        /// Enforces that the given hash length is greater than a certain value
        /// </summary>
        /// <param name="key">The key of the hash to check.</param>
        /// <param name="length">The length the hash must be greater than.</param>
        public static Condition HashLengthGreaterThan(RedisKey key, long length) => new LengthCondition(key, RedisType.Hash, -1, length);

        /// <summary>
        /// Enforces that the given string length is a certain value
        /// </summary>
        /// <param name="key">The key of the string to check.</param>
        /// <param name="length">The length the string must be equal to.</param>
        public static Condition StringLengthEqual(RedisKey key, long length) => new LengthCondition(key, RedisType.String, 0, length);

        /// <summary>
        /// Enforces that the given string length is less than a certain value
        /// </summary>
        /// <param name="key">The key of the string to check.</param>
        /// <param name="length">The length the string must be less than.</param>
        public static Condition StringLengthLessThan(RedisKey key, long length) => new LengthCondition(key, RedisType.String, 1, length);

        /// <summary>
        /// Enforces that the given string length is greater than a certain value
        /// </summary>
        /// <param name="key">The key of the string to check.</param>
        /// <param name="length">The length the string must be greater than.</param>
        public static Condition StringLengthGreaterThan(RedisKey key, long length) => new LengthCondition(key, RedisType.String, -1, length);

        /// <summary>
        /// Enforces that the given list length is a certain value
        /// </summary>
        /// <param name="key">The key of the list to check.</param>
        /// <param name="length">The length the list must be equal to.</param>
        public static Condition ListLengthEqual(RedisKey key, long length) => new LengthCondition(key, RedisType.List, 0, length);

        /// <summary>
        /// Enforces that the given list length is less than a certain value
        /// </summary>
        /// <param name="key">The key of the list to check.</param>
        /// <param name="length">The length the list must be less than.</param>
        public static Condition ListLengthLessThan(RedisKey key, long length) => new LengthCondition(key, RedisType.List, 1, length);

        /// <summary>
        /// Enforces that the given list length is greater than a certain value
        /// </summary>
        /// <param name="key">The key of the list to check.</param>
        /// <param name="length">The length the list must be greater than.</param>
        public static Condition ListLengthGreaterThan(RedisKey key, long length) => new LengthCondition(key, RedisType.List, -1, length);

        /// <summary>
        /// Enforces that the given set cardinality is a certain value
        /// </summary>
        /// <param name="key">The key of the set to check.</param>
        /// <param name="length">The length the set must be equal to.</param>
        public static Condition SetLengthEqual(RedisKey key, long length) => new LengthCondition(key, RedisType.Set, 0, length);

        /// <summary>
        /// Enforces that the given set cardinality is less than a certain value
        /// </summary>
        /// <param name="key">The key of the set to check.</param>
        /// <param name="length">The length the set must be less than.</param>
        public static Condition SetLengthLessThan(RedisKey key, long length) => new LengthCondition(key, RedisType.Set, 1, length);

        /// <summary>
        /// Enforces that the given set cardinality is greater than a certain value
        /// </summary>
        /// <param name="key">The key of the set to check.</param>
        /// <param name="length">The length the set must be greater than.</param>
        public static Condition SetLengthGreaterThan(RedisKey key, long length) => new LengthCondition(key, RedisType.Set, -1, length);

        /// <summary>
        /// Enforces that the given set contains a certain member
        /// </summary>
        /// <param name="key">The key of the set to check.</param>
        /// <param name="member">The member the set must contain.</param>
        public static Condition SetContains(RedisKey key, RedisValue member) => new ExistsCondition(key, RedisType.Set, member, true);

        /// <summary>
        /// Enforces that the given set does not contain a certain member
        /// </summary>
        /// <param name="key">The key of the set to check.</param>
        /// <param name="member">The member the set must not contain.</param>
        public static Condition SetNotContains(RedisKey key, RedisValue member) => new ExistsCondition(key, RedisType.Set, member, false);

        /// <summary>
        /// Enforces that the given sorted set cardinality is a certain value
        /// </summary>
        /// <param name="key">The key of the sorted set to check.</param>
        /// <param name="length">The length the sorted set must be equal to.</param>
        public static Condition SortedSetLengthEqual(RedisKey key, long length) => new LengthCondition(key, RedisType.SortedSet, 0, length);

        /// <summary>
        /// Enforces that the given sorted set contains a certain number of members with scores in the given range
        /// </summary>
        /// <param name="key">The key of the sorted set to check.</param>
        /// <param name="length">The length the sorted set must be equal to.</param>
        /// <param name="min">Minimum inclusive score.</param>
        /// <param name="max">Maximum inclusive score.</param>
        public static Condition SortedSetLengthEqual(RedisKey key, long length, double min = double.NegativeInfinity, double max = double.PositiveInfinity) => new SortedSetRangeLengthCondition(key, min, max, 0, length);

        /// <summary>
        /// Enforces that the given sorted set cardinality is less than a certain value
        /// </summary>
        /// <param name="key">The key of the sorted set to check.</param>
        /// <param name="length">The length the sorted set must be less than.</param>
        public static Condition SortedSetLengthLessThan(RedisKey key, long length) => new LengthCondition(key, RedisType.SortedSet, 1, length);

        /// <summary>
        /// Enforces that the given sorted set contains less than a certain number of members with scores in the given range
        /// </summary>
        /// <param name="key">The key of the sorted set to check.</param>
        /// <param name="length">The length the sorted set must be equal to.</param>
        /// <param name="min">Minimum inclusive score.</param>
        /// <param name="max">Maximum inclusive score.</param>
        public static Condition SortedSetLengthLessThan(RedisKey key, long length, double min = double.NegativeInfinity, double max = double.PositiveInfinity) => new SortedSetRangeLengthCondition(key, min, max, 1, length);

        /// <summary>
        /// Enforces that the given sorted set cardinality is greater than a certain value
        /// </summary>
        /// <param name="key">The key of the sorted set to check.</param>
        /// <param name="length">The length the sorted set must be greater than.</param>
        public static Condition SortedSetLengthGreaterThan(RedisKey key, long length) => new LengthCondition(key, RedisType.SortedSet, -1, length);

        /// <summary>
        /// Enforces that the given sorted set contains more than a certain number of members with scores in the given range
        /// </summary>
        /// <param name="key">The key of the sorted set to check.</param>
        /// <param name="length">The length the sorted set must be equal to.</param>
        /// <param name="min">Minimum inclusive score.</param>
        /// <param name="max">Maximum inclusive score.</param>
        public static Condition SortedSetLengthGreaterThan(RedisKey key, long length, double min = double.NegativeInfinity, double max = double.PositiveInfinity) => new SortedSetRangeLengthCondition(key, min, max, -1, length);

        /// <summary>
        /// Enforces that the given sorted set contains a certain member
        /// </summary>
        /// <param name="key">The key of the sorted set to check.</param>
        /// <param name="member">The member the sorted set must contain.</param>
        public static Condition SortedSetContains(RedisKey key, RedisValue member) => new ExistsCondition(key, RedisType.SortedSet, member, true);

        /// <summary>
        /// Enforces that the given sorted set does not contain a certain member
        /// </summary>
        /// <param name="key">The key of the sorted set to check.</param>
        /// <param name="member">The member the sorted set must not contain.</param>
        public static Condition SortedSetNotContains(RedisKey key, RedisValue member) => new ExistsCondition(key, RedisType.SortedSet, member, false);

        /// <summary>
        /// Enforces that the given sorted set member must have the specified score.
        /// </summary>
        /// <param name="key">The key of the sorted set to check.</param>
        /// <param name="member">The member the sorted set to check.</param>
        /// <param name="score">The score that member must have.</param>
        public static Condition SortedSetEqual(RedisKey key, RedisValue member, RedisValue score) => new EqualsCondition(key, RedisType.SortedSet, member, true, score);

        /// <summary>
        /// Enforces that the given sorted set member must not have the specified score.
        /// </summary>
        /// <param name="key">The key of the sorted set to check.</param>
        /// <param name="member">The member the sorted set to check.</param>
        /// <param name="score">The score that member must not have.</param>
        public static Condition SortedSetNotEqual(RedisKey key, RedisValue member, RedisValue score) => new EqualsCondition(key, RedisType.SortedSet, member, false, score);

        /// <summary>
        /// Enforces that the given sorted set must have the given score.
        /// </summary>
        /// <param name="key">The key of the sorted set to check.</param>
        /// <param name="score">The score that the sorted set must have.</param>
        public static Condition SortedSetScoreExists(RedisKey key, RedisValue score) => new SortedSetScoreCondition(key, score, false, 0);

        /// <summary>
        /// Enforces that the given sorted set must not have the given score.
        /// </summary>
        /// <param name="key">The key of the sorted set to check.</param>
        /// <param name="score">The score that the sorted set must not have.</param>
        public static Condition SortedSetScoreNotExists(RedisKey key, RedisValue score) => new SortedSetScoreCondition(key, score, true, 0);

        /// <summary>
        /// Enforces that the given sorted set must have the specified count of the given score.
        /// </summary>
        /// <param name="key">The key of the sorted set to check.</param>
        /// <param name="score">The score that the sorted set must have.</param>
        /// <param name="count">The number of members which sorted set must have.</param>
        public static Condition SortedSetScoreExists(RedisKey key, RedisValue score, RedisValue count) => new SortedSetScoreCondition(key, score, true, count);

        /// <summary>
        /// Enforces that the given sorted set must not have the specified count of the given score.
        /// </summary>
        /// <param name="key">The key of the sorted set to check.</param>
        /// <param name="score">The score that the sorted set must not have.</param>
        /// <param name="count">The number of members which sorted set must not have.</param>
        public static Condition SortedSetScoreNotExists(RedisKey key, RedisValue score, RedisValue count) => new SortedSetScoreCondition(key, score, false, count);

#pragma warning restore RCS1231

        internal abstract void CheckCommands(CommandMap commandMap);

        internal abstract IEnumerable<Message> CreateMessages(int db, IResultBox resultBox);

        internal abstract int GetHashSlot(ServerSelectionStrategy serverSelectionStrategy);
        internal abstract bool TryValidate(in RawResult result, out bool value);

        internal sealed class ConditionProcessor : ResultProcessor<bool>
        {
            public static readonly ConditionProcessor Default = new ConditionProcessor();

#pragma warning disable RCS1231 // Make parameter ref read-only.
            public static Message CreateMessage(Condition condition, int db, CommandFlags flags, RedisCommand command, in RedisKey key, RedisValue value = default(RedisValue))
#pragma warning restore RCS1231 // Make parameter ref read-only.
            {
                return new ConditionMessage(condition, db, flags, command, key, value);
            }

            public static Message CreateMessage(Condition condition, int db, CommandFlags flags, RedisCommand command, in RedisKey key, in RedisValue value, in RedisValue value1)
            {
                return new ConditionMessage(condition, db, flags, command, key, value, value1);
            }

            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                connection?.BridgeCouldBeNull?.Multiplexer?.OnTransactionLog($"condition '{message.CommandAndKey}' got '{result.ToString()}'");
                var msg = message as ConditionMessage;
                var condition = msg?.Condition;
                if (condition != null && condition.TryValidate(result, out bool final))
                {
                    SetResult(message, final);
                    return true;
                }
                return false;
            }

            private class ConditionMessage : Message.CommandKeyBase
            {
                public readonly Condition Condition;
                private readonly RedisValue value;
                private readonly RedisValue value1;

                public ConditionMessage(Condition condition, int db, CommandFlags flags, RedisCommand command, in RedisKey key, in RedisValue value)
                    : base(db, flags, command, key)
                {
                    Condition = condition;
                    this.value = value; // note no assert here
                }

                public ConditionMessage(Condition condition, int db, CommandFlags flags, RedisCommand command, in RedisKey key, in RedisValue value, in RedisValue value1)
                    : this(condition, db, flags, command, key, value)
                {
                    this.value1 = value1; // note no assert here
                }

                protected override void WriteImpl(PhysicalConnection physical)
                {
                    if (value.IsNull)
                    {
                        physical.WriteHeader(command, 1);
                        physical.Write(Key);
                    }
                    else
                    {
                        physical.WriteHeader(command, value1.IsNull ? 2 : 3);
                        physical.Write(Key);
                        physical.WriteBulkString(value);
                        if (!value1.IsNull)
                        {
                            physical.WriteBulkString(value1);
                        }
                    }
                }
                public override int ArgCount => value.IsNull ? 1 : value1.IsNull ? 2 : 3;
            }
        }

        internal class ExistsCondition : Condition
        {
            private readonly bool expectedResult;
            private readonly RedisValue expectedValue;
            private readonly RedisKey key;
            private readonly RedisType type;
            private readonly RedisCommand cmd;

            internal override Condition MapKeys(Func<RedisKey, RedisKey> map)
            {
                return new ExistsCondition(map(key), type, expectedValue, expectedResult);
            }

            public ExistsCondition(in RedisKey key, RedisType type, in RedisValue expectedValue, bool expectedResult)
            {
                if (key.IsNull) throw new ArgumentException("key");
                this.key = key;
                this.type = type;
                this.expectedValue = expectedValue;
                this.expectedResult = expectedResult;

                if (expectedValue.IsNull)
                {
                    cmd = RedisCommand.EXISTS;
                }
                else
                {
                    switch (type)
                    {
                        case RedisType.Hash:
                            cmd = RedisCommand.HEXISTS;
                            break;

                        case RedisType.Set:
                            cmd = RedisCommand.SISMEMBER;
                            break;

                        case RedisType.SortedSet:
                            cmd = RedisCommand.ZSCORE;
                            break;

                        default:
                            throw new ArgumentException(nameof(type));
                    }
                }
            }

            public override string ToString()
            {
                return (expectedValue.IsNull ? key.ToString() : ((string)key) + " " + type + " > " + expectedValue)
                    + (expectedResult ? " exists" : " does not exists");
            }

            internal override void CheckCommands(CommandMap commandMap) => commandMap.AssertAvailable(cmd);

            internal override IEnumerable<Message> CreateMessages(int db, IResultBox resultBox)
            {
                yield return Message.Create(db, CommandFlags.None, RedisCommand.WATCH, key);

                var message = ConditionProcessor.CreateMessage(this, db, CommandFlags.None, cmd, key, expectedValue);
                message.SetSource(ConditionProcessor.Default, resultBox);
                yield return message;
            }

            internal override int GetHashSlot(ServerSelectionStrategy serverSelectionStrategy) => serverSelectionStrategy.HashSlot(key);

            internal override bool TryValidate(in RawResult result, out bool value)
            {
                switch (type)
                {
                    case RedisType.SortedSet:
                        var parsedValue = result.AsRedisValue();
                        value = (parsedValue.IsNull != expectedResult);
                        ConnectionMultiplexer.TraceWithoutContext("exists: " + parsedValue + "; expected: " + expectedResult + "; voting: " + value);
                        return true;

                    default:
                        bool parsed;
                        if (ResultProcessor.DemandZeroOrOneProcessor.TryGet(result, out parsed))
                        {
                            value = parsed == expectedResult;
                            ConnectionMultiplexer.TraceWithoutContext("exists: " + parsed + "; expected: " + expectedResult + "; voting: " + value);
                            return true;
                        }
                        value = false;
                        return false;
                }
            }
        }

        internal class EqualsCondition : Condition
        {
            internal override Condition MapKeys(Func<RedisKey, RedisKey> map)
            {
                return new EqualsCondition(map(key), type, memberName, expectedEqual, expectedValue);
            }

            private readonly bool expectedEqual;
            private readonly RedisValue memberName, expectedValue;
            private readonly RedisKey key;
            private readonly RedisType type;
            private readonly RedisCommand cmd;

            public EqualsCondition(in RedisKey key, RedisType type, in RedisValue memberName, bool expectedEqual, in RedisValue expectedValue)
            {
                if (key.IsNull) throw new ArgumentException("key");
                this.key = key;
                this.memberName = memberName;
                this.expectedEqual = expectedEqual;
                this.expectedValue = expectedValue;
                this.type = type;
                switch (type)
                {
                    case RedisType.Hash:
                        cmd = memberName.IsNull ? RedisCommand.GET : RedisCommand.HGET;
                        break;

                    case RedisType.SortedSet:
                        cmd = RedisCommand.ZSCORE;
                        break;

                    default:
                        throw new ArgumentException(nameof(type));
                }
            }

            public override string ToString()
            {
                return (memberName.IsNull ? key.ToString() : ((string)key) + " " + type + " > " + memberName)
                    + (expectedEqual ? " == " : " != ")
                    + expectedValue;
            }

            internal override void CheckCommands(CommandMap commandMap) => commandMap.AssertAvailable(cmd);

            internal sealed override IEnumerable<Message> CreateMessages(int db, IResultBox resultBox)
            {
                yield return Message.Create(db, CommandFlags.None, RedisCommand.WATCH, key);

                var message = ConditionProcessor.CreateMessage(this, db, CommandFlags.None, cmd, key, memberName);
                message.SetSource(ConditionProcessor.Default, resultBox);
                yield return message;
            }

            internal override int GetHashSlot(ServerSelectionStrategy serverSelectionStrategy)
            {
                return serverSelectionStrategy.HashSlot(key);
            }

            internal override bool TryValidate(in RawResult result, out bool value)
            {
                switch (type)
                {
                    case RedisType.SortedSet:
                        var parsedValue = RedisValue.Null;
                        if (!result.IsNull)
                        {
                            if (result.TryGetDouble(out var val))
                            {
                                parsedValue = val;
                            }
                        }

                        value = (parsedValue == expectedValue) == expectedEqual;
                        ConnectionMultiplexer.TraceWithoutContext("actual: " + (string)parsedValue + "; expected: " + (string)expectedValue +
                                                                  "; wanted: " + (expectedEqual ? "==" : "!=") + "; voting: " + value);
                        return true;

                    default:
                        switch (result.Type)
                        {
                            case ResultType.BulkString:
                            case ResultType.SimpleString:
                            case ResultType.Integer:
                                var parsed = result.AsRedisValue();
                                value = (parsed == expectedValue) == expectedEqual;
                                ConnectionMultiplexer.TraceWithoutContext("actual: " + (string)parsed + "; expected: " + (string)expectedValue +
                                                                          "; wanted: " + (expectedEqual ? "==" : "!=") + "; voting: " + value);
                                return true;
                        }
                        value = false;
                        return false;
                }
            }
        }

        internal class ListCondition : Condition
        {
            internal override Condition MapKeys(Func<RedisKey, RedisKey> map)
            {
                return new ListCondition(map(key), index, expectedResult, expectedValue);
            }

            private readonly bool expectedResult;
            private readonly long index;
            private readonly RedisValue? expectedValue;
            private readonly RedisKey key;
            public ListCondition(in RedisKey key, long index, bool expectedResult, in RedisValue? expectedValue)
            {
                if (key.IsNull) throw new ArgumentException(nameof(key));
                this.key = key;
                this.index = index;
                this.expectedResult = expectedResult;
                this.expectedValue = expectedValue;
            }

            public override string ToString()
            {
                return ((string)key) + "[" + index.ToString() + "]"
                    + (expectedValue.HasValue ? (expectedResult ? " == " : " != ") + expectedValue.Value : (expectedResult ? " exists" : " does not exist"));
            }

            internal override void CheckCommands(CommandMap commandMap)
            {
                commandMap.AssertAvailable(RedisCommand.LINDEX);
            }

            internal sealed override IEnumerable<Message> CreateMessages(int db, IResultBox resultBox)
            {
                yield return Message.Create(db, CommandFlags.None, RedisCommand.WATCH, key);

                var message = ConditionProcessor.CreateMessage(this, db, CommandFlags.None, RedisCommand.LINDEX, key, index);
                message.SetSource(ConditionProcessor.Default, resultBox);
                yield return message;
            }

            internal override int GetHashSlot(ServerSelectionStrategy serverSelectionStrategy) => serverSelectionStrategy.HashSlot(key);

            internal override bool TryValidate(in RawResult result, out bool value)
            {
                switch (result.Type)
                {
                    case ResultType.BulkString:
                    case ResultType.SimpleString:
                    case ResultType.Integer:
                        var parsed = result.AsRedisValue();
                        if (expectedValue.HasValue)
                        {
                            value = (parsed == expectedValue.Value) == expectedResult;
                            ConnectionMultiplexer.TraceWithoutContext("actual: " + (string)parsed + "; expected: " + (string)expectedValue.Value +
                                "; wanted: " + (expectedResult ? "==" : "!=") + "; voting: " + value);
                        }
                        else
                        {
                            value = (parsed.IsNull != expectedResult);
                            ConnectionMultiplexer.TraceWithoutContext("exists: " + parsed + "; expected: " + expectedResult + "; voting: " + value);
                        }
                        return true;
                }
                value = false;
                return false;
            }
        }

        internal class LengthCondition : Condition
        {
            internal override Condition MapKeys(Func<RedisKey, RedisKey> map)
            {
                return new LengthCondition(map(key), type, compareToResult, expectedLength);
            }

            private readonly int compareToResult;
            private readonly long expectedLength;
            private readonly RedisKey key;
            private readonly RedisType type;
            private readonly RedisCommand cmd;

            public LengthCondition(in RedisKey key, RedisType type, int compareToResult, long expectedLength)
            {
                if (key.IsNull) throw new ArgumentException(nameof(key));
                this.key = key;
                this.compareToResult = compareToResult;
                this.expectedLength = expectedLength;
                this.type = type;
                switch (type)
                {
                    case RedisType.Hash:
                        cmd = RedisCommand.HLEN;
                        break;

                    case RedisType.Set:
                        cmd = RedisCommand.SCARD;
                        break;

                    case RedisType.List:
                        cmd = RedisCommand.LLEN;
                        break;

                    case RedisType.SortedSet:
                        cmd = RedisCommand.ZCARD;
                        break;

                    case RedisType.String:
                        cmd = RedisCommand.STRLEN;
                        break;

                    default:
                        throw new ArgumentException(nameof(type));
                }
            }

            public override string ToString()
            {
                return ((string)key) + " " + type + " length" + GetComparisonString() + expectedLength;
            }

            private string GetComparisonString()
            {
                return compareToResult == 0 ? " == " : (compareToResult < 0 ? " > " : " < ");
            }

            internal override void CheckCommands(CommandMap commandMap)
            {
                commandMap.AssertAvailable(cmd);
            }

            internal sealed override IEnumerable<Message> CreateMessages(int db, IResultBox resultBox)
            {
                yield return Message.Create(db, CommandFlags.None, RedisCommand.WATCH, key);

                var message = ConditionProcessor.CreateMessage(this, db, CommandFlags.None, cmd, key);
                message.SetSource(ConditionProcessor.Default, resultBox);
                yield return message;
            }

            internal override int GetHashSlot(ServerSelectionStrategy serverSelectionStrategy)
            {
                return serverSelectionStrategy.HashSlot(key);
            }

            internal override bool TryValidate(in RawResult result, out bool value)
            {
                switch (result.Type)
                {
                    case ResultType.BulkString:
                    case ResultType.SimpleString:
                    case ResultType.Integer:
                        var parsed = result.AsRedisValue();
                        value = parsed.IsInteger && (expectedLength.CompareTo((long)parsed) == compareToResult);
                        ConnectionMultiplexer.TraceWithoutContext("actual: " + (string)parsed + "; expected: " + expectedLength +
                            "; wanted: " + GetComparisonString() + "; voting: " + value);
                        return true;
                }
                value = false;
                return false;
            }
        }

        internal class SortedSetRangeLengthCondition : Condition
        {
            internal override Condition MapKeys(Func<RedisKey, RedisKey> map)
            {
                return new SortedSetRangeLengthCondition(map(key), min, max, compareToResult, expectedLength);
            }

            private readonly RedisValue min;
            private readonly RedisValue max;
            private readonly int compareToResult;
            private readonly long expectedLength;
            private readonly RedisKey key;

            public SortedSetRangeLengthCondition(in RedisKey key, RedisValue min, RedisValue max, int compareToResult, long expectedLength)
            {
                if (key.IsNull) throw new ArgumentException(nameof(key));
                this.key = key;
                this.min = min;
                this.max = max;
                this.compareToResult = compareToResult;
                this.expectedLength = expectedLength;
            }

            public override string ToString()
            {
                return ((string)key) + " " + RedisType.SortedSet + " range[" + min + ", " + max + "] length" + GetComparisonString() + expectedLength;
            }

            private string GetComparisonString()
            {
                return compareToResult == 0 ? " == " : (compareToResult < 0 ? " > " : " < ");
            }

            internal override void CheckCommands(CommandMap commandMap)
            {
                commandMap.AssertAvailable(RedisCommand.ZCOUNT);
            }

            internal sealed override IEnumerable<Message> CreateMessages(int db, IResultBox resultBox)
            {
                yield return Message.Create(db, CommandFlags.None, RedisCommand.WATCH, key);

                var message = ConditionProcessor.CreateMessage(this, db, CommandFlags.None, RedisCommand.ZCOUNT, key, min, max);
                message.SetSource(ConditionProcessor.Default, resultBox);
                yield return message;
            }

            internal override int GetHashSlot(ServerSelectionStrategy serverSelectionStrategy)
            {
                return serverSelectionStrategy.HashSlot(key);
            }

            internal override bool TryValidate(in RawResult result, out bool value)
            {
                switch (result.Type)
                {
                    case ResultType.BulkString:
                    case ResultType.SimpleString:
                    case ResultType.Integer:
                        var parsed = result.AsRedisValue();
                        value = parsed.IsInteger && (expectedLength.CompareTo((long)parsed) == compareToResult);
                        ConnectionMultiplexer.TraceWithoutContext("actual: " + (string)parsed + "; expected: " + expectedLength +
                            "; wanted: " + GetComparisonString() + "; voting: " + value);
                        return true;
                }
                value = false;
                return false;
            }
        }

        internal class SortedSetScoreCondition : Condition
        {
            internal override Condition MapKeys(Func<RedisKey, RedisKey> map)
            {
                return new SortedSetScoreCondition(map(key), sortedSetScore, expectedEqual, expectedValue);
            }

            private readonly bool expectedEqual;
            private readonly RedisValue sortedSetScore, expectedValue;
            private readonly RedisKey key;

            public SortedSetScoreCondition(in RedisKey key, in RedisValue sortedSetScore, bool expectedEqual, in RedisValue expectedValue)
            {
                if (key.IsNull)
                {
                    throw new ArgumentException("key");
                }

                this.key = key;
                this.sortedSetScore = sortedSetScore;
                this.expectedEqual = expectedEqual;
                this.expectedValue = expectedValue;
            }

            public override string ToString()
            {
                return key.ToString() + (expectedEqual ? " contains " : " not contains ") + expectedValue + " members with score: " + sortedSetScore;
            }

            internal override void CheckCommands(CommandMap commandMap) => commandMap.AssertAvailable(RedisCommand.ZCOUNT);

            internal sealed override IEnumerable<Message> CreateMessages(int db, IResultBox resultBox)
            {
                yield return Message.Create(db, CommandFlags.None, RedisCommand.WATCH, key);

                var message = ConditionProcessor.CreateMessage(this, db, CommandFlags.None, RedisCommand.ZCOUNT, key, sortedSetScore, sortedSetScore);
                message.SetSource(ConditionProcessor.Default, resultBox);

                yield return message;
            }

            internal override int GetHashSlot(ServerSelectionStrategy serverSelectionStrategy) => serverSelectionStrategy.HashSlot(key);

            internal override bool TryValidate(in RawResult result, out bool value)
            {
                switch (result.Type)
                {
                    case ResultType.Integer:
                        var parsedValue = result.AsRedisValue();
                        value = (parsedValue == expectedValue) == expectedEqual;
                        ConnectionMultiplexer.TraceWithoutContext("actual: " + (string)parsedValue + "; expected: " + (string)expectedValue + "; wanted: " + (expectedEqual ? "==" : "!=") + "; voting: " + value);
                        return true;
                }

                value = false;
                return false;
            }
        }
    }

    /// <summary>
    /// Indicates the status of a condition as part of a transaction
    /// </summary>
    public sealed class ConditionResult
    {
        internal readonly Condition Condition;

        private IResultBox<bool> resultBox;

        private volatile bool wasSatisfied;

        internal ConditionResult(Condition condition)
        {
            Condition = condition;
            resultBox = SimpleResultBox<bool>.Create();
        }

        /// <summary>
        /// Indicates whether the condition was satisfied
        /// </summary>
        public bool WasSatisfied => wasSatisfied;

        internal IEnumerable<Message> CreateMessages(int db) => Condition.CreateMessages(db, resultBox);

        internal IResultBox<bool> GetBox() { return resultBox; }
        internal bool UnwrapBox()
        {
            if (resultBox != null)
            {
                bool val = resultBox.GetResult(out var ex);
                resultBox = null;
                wasSatisfied = ex == null && val;
            }
            return wasSatisfied;
        }
    }
}
