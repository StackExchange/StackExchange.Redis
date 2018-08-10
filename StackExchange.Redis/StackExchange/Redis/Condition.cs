using System;
using System.Collections.Generic;

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
            return new EqualsCondition(key, hashField, true, value);
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
            return new EqualsCondition(key, hashField, false, value);
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
            return new EqualsCondition(key, RedisValue.Null, true, value);
        }

        /// <summary>
        /// Enforces that the given key must not have the specified value
        /// </summary>
        /// <param name="key">The key to check.</param>
        /// <param name="value">The value that must not match.</param>
        public static Condition StringNotEqual(RedisKey key, RedisValue value)
        {
            if (value.IsNull) return KeyExists(key);
            return new EqualsCondition(key, RedisValue.Null, false, value);
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
        /// Enforces that the given sorted set cardinality is less than a certain value
        /// </summary>
        /// <param name="key">The key of the sorted set to check.</param>
        /// <param name="length">The length the sorted set must be less than.</param>
        public static Condition SortedSetLengthLessThan(RedisKey key, long length) => new LengthCondition(key, RedisType.SortedSet, 1, length);

        /// <summary>
        /// Enforces that the given sorted set cardinality is greater than a certain value
        /// </summary>
        /// <param name="key">The key of the sorted set to check.</param>
        /// <param name="length">The length the sorted set must be greater than.</param>
        public static Condition SortedSetLengthGreaterThan(RedisKey key, long length) => new LengthCondition(key, RedisType.SortedSet, -1, length);

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

        internal abstract void CheckCommands(CommandMap commandMap);

        internal abstract IEnumerable<Message> CreateMessages(int db, ResultBox resultBox);

        internal abstract int GetHashSlot(ServerSelectionStrategy serverSelectionStrategy);
        internal abstract bool TryValidate(RawResult result, out bool value);

        internal sealed class ConditionProcessor : ResultProcessor<bool>
        {
            public static readonly ConditionProcessor Default = new ConditionProcessor();

            public static Message CreateMessage(Condition condition, int db, CommandFlags flags, RedisCommand command, RedisKey key, RedisValue value = default(RedisValue))
            {
                return new ConditionMessage(condition, db, flags, command, key, value);
            }

            protected override bool SetResultCore(PhysicalConnection connection, Message message, RawResult result)
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
                private RedisValue value;

                public ConditionMessage(Condition condition, int db, CommandFlags flags, RedisCommand command, RedisKey key, RedisValue value)
                    : base(db, flags, command, key)
                {
                    Condition = condition;
                    this.value = value; // note no assert here
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
                        physical.WriteHeader(command, 2);
                        physical.Write(Key);
                        physical.WriteBulkString(value);
                    }
                }
                public override int ArgCount => value.IsNull ? 1 : 2;
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

            public ExistsCondition(RedisKey key, RedisType type, RedisValue expectedValue, bool expectedResult)
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

            internal override IEnumerable<Message> CreateMessages(int db, ResultBox resultBox)
            {
                yield return Message.Create(db, CommandFlags.None, RedisCommand.WATCH, key);

                var message = ConditionProcessor.CreateMessage(this, db, CommandFlags.None, cmd, key, expectedValue);
                message.SetSource(ConditionProcessor.Default, resultBox);
                yield return message;
            }

            internal override int GetHashSlot(ServerSelectionStrategy serverSelectionStrategy) => serverSelectionStrategy.HashSlot(key);

            internal override bool TryValidate(RawResult result, out bool value)
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
                return new EqualsCondition(map(key), hashField, expectedEqual, expectedValue);
            }

            private readonly bool expectedEqual;
            private readonly RedisValue hashField, expectedValue;
            private readonly RedisKey key;
            public EqualsCondition(RedisKey key, RedisValue hashField, bool expectedEqual, RedisValue expectedValue)
            {
                if (key.IsNull) throw new ArgumentException("key");
                this.key = key;
                this.hashField = hashField;
                this.expectedEqual = expectedEqual;
                this.expectedValue = expectedValue;
            }

            public override string ToString()
            {
                return (hashField.IsNull ? key.ToString() : ((string)key) + " > " + hashField)
                    + (expectedEqual ? " == " : " != ")
                    + expectedValue;
            }

            internal override void CheckCommands(CommandMap commandMap)
            {
                commandMap.AssertAvailable(hashField.IsNull ? RedisCommand.GET : RedisCommand.HGET);
            }

            internal sealed override IEnumerable<Message> CreateMessages(int db, ResultBox resultBox)
            {
                yield return Message.Create(db, CommandFlags.None, RedisCommand.WATCH, key);

                var cmd = hashField.IsNull ? RedisCommand.GET : RedisCommand.HGET;
                var message = ConditionProcessor.CreateMessage(this, db, CommandFlags.None, cmd, key, hashField);
                message.SetSource(ConditionProcessor.Default, resultBox);
                yield return message;
            }

            internal override int GetHashSlot(ServerSelectionStrategy serverSelectionStrategy)
            {
                return serverSelectionStrategy.HashSlot(key);
            }

            internal override bool TryValidate(RawResult result, out bool value)
            {
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
            public ListCondition(RedisKey key, long index, bool expectedResult, RedisValue? expectedValue)
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

            internal sealed override IEnumerable<Message> CreateMessages(int db, ResultBox resultBox)
            {
                yield return Message.Create(db, CommandFlags.None, RedisCommand.WATCH, key);

                var message = ConditionProcessor.CreateMessage(this, db, CommandFlags.None, RedisCommand.LINDEX, key, index);
                message.SetSource(ConditionProcessor.Default, resultBox);
                yield return message;
            }

            internal override int GetHashSlot(ServerSelectionStrategy serverSelectionStrategy) => serverSelectionStrategy.HashSlot(key);

            internal override bool TryValidate(RawResult result, out bool value)
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

            public LengthCondition(RedisKey key, RedisType type, int compareToResult, long expectedLength)
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

            internal sealed override IEnumerable<Message> CreateMessages(int db, ResultBox resultBox)
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

            internal override bool TryValidate(RawResult result, out bool value)
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
    }

    /// <summary>
    /// Indicates the status of a condition as part of a transaction
    /// </summary>
    public sealed class ConditionResult
    {
        internal readonly Condition Condition;

        private ResultBox<bool> resultBox;

        private volatile bool wasSatisfied;

        internal ConditionResult(Condition condition)
        {
            Condition = condition;
            resultBox = ResultBox<bool>.Get(condition);
        }

        /// <summary>
        /// Indicates whether the condition was satisfied
        /// </summary>
        public bool WasSatisfied => wasSatisfied;

        internal IEnumerable<Message> CreateMessages(int db) => Condition.CreateMessages(db, resultBox);

        internal ResultBox<bool> GetBox() { return resultBox; }
        internal bool UnwrapBox()
        {
            if (resultBox != null)
            {
                ResultBox<bool>.UnwrapAndRecycle(resultBox, false, out bool val, out Exception ex);
                resultBox = null;
                wasSatisfied = ex == null && val;
            }
            return wasSatisfied;
        }
    }
}
