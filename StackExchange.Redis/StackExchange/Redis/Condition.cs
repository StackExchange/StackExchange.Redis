﻿using System;
using System.Collections.Generic;

namespace StackExchange.Redis
{

    /// <summary>
    /// Describes a pre-condition used in a redis transaction
    /// </summary>
    public abstract class Condition
    {
        internal abstract Condition MapKeys(Func<RedisKey,RedisKey> map);

        private Condition() { }

        /// <summary>
        /// Enforces that the given hash-field must have the specified value
        /// </summary>
        public static Condition HashEqual(RedisKey key, RedisValue hashField, RedisValue value)
        {
            if (hashField.IsNull) throw new ArgumentNullException(nameof(hashField));
            if (value.IsNull) return HashNotExists(key, hashField);
            return new EqualsCondition(key, hashField, true, value);
        }

        /// <summary>
        /// Enforces that the given hash-field must exist
        /// </summary>
        public static Condition HashExists(RedisKey key, RedisValue hashField)
        {
            if (hashField.IsNull) throw new ArgumentNullException(nameof(hashField));
            return new ExistsCondition(key, hashField, true);
        }

        /// <summary>
        /// Enforces that the given hash-field must not have the specified value
        /// </summary>
        public static Condition HashNotEqual(RedisKey key, RedisValue hashField, RedisValue value)
        {
            if (hashField.IsNull) throw new ArgumentNullException(nameof(hashField));
            if (value.IsNull) return HashExists(key, hashField);
            return new EqualsCondition(key, hashField, false, value);
        }

        /// <summary>
        /// Enforces that the given hash-field must not exist
        /// </summary>
        public static Condition HashNotExists(RedisKey key, RedisValue hashField)
        {
            if (hashField.IsNull) throw new ArgumentNullException(nameof(hashField));
            return new ExistsCondition(key, hashField, false);
        }

        /// <summary>
        /// Enforces that the given key must exist
        /// </summary>
        public static Condition KeyExists(RedisKey key)
        {
            return new ExistsCondition(key, RedisValue.Null, true);
        }

        /// <summary>
        /// Enforces that the given key must not exist
        /// </summary>
        public static Condition KeyNotExists(RedisKey key)
        {
            return new ExistsCondition(key, RedisValue.Null, false);
        }

        /// <summary>
        /// Enforces that the given list index must have the specified value 
        /// </summary>
        public static Condition ListIndexEqual(RedisKey key, long index, RedisValue value)
        {
            return new ListCondition(key, index, true, value);
        }

        /// <summary>
        /// Enforces that the given list index must exist
        /// </summary>
        public static Condition ListIndexExists(RedisKey key, long index)
        {
            return new ListCondition(key, index, true, null);
        }

        /// <summary>
        /// Enforces that the given list index must not have the specified value 
        /// </summary>
        public static Condition ListIndexNotEqual(RedisKey key, long index, RedisValue value)
        {
            return new ListCondition(key, index, false, value);
        }

        /// <summary>
        /// Enforces that the given list index must not exist
        /// </summary>
        public static Condition ListIndexNotExists(RedisKey key, long index)
        {
            return new ListCondition(key, index, false, null);
        }

        /// <summary>
        /// Enforces that the given key must have the specified value
        /// </summary>
        public static Condition StringEqual(RedisKey key, RedisValue value)
        {
            if (value.IsNull) return KeyNotExists(key);
            return new EqualsCondition(key, RedisValue.Null, true, value);
        }

        /// <summary>
        /// Enforces that the given key must not have the specified value
        /// </summary>
        public static Condition StringNotEqual(RedisKey key, RedisValue value)
        {
            if (value.IsNull) return KeyExists(key);
            return new EqualsCondition(key, RedisValue.Null, false, value);
        }

        /// <summary>
        /// Enforces that the given hash length is a certain value
        /// </summary>
        public static Condition HashLengthEqual(RedisKey key, long length)
        {
            return new LengthCondition(key, RedisType.Hash, 0, length);
        }
        
        /// <summary>
        /// Enforces that the given hash length is less than a certain value
        /// </summary>
        public static Condition HashLengthLessThan(RedisKey key, long length)
        {
            return new LengthCondition(key, RedisType.Hash, 1, length);
        }
        
        /// <summary>
        /// Enforces that the given hash length is greater than a certain value
        /// </summary>
        public static Condition HashLengthGreaterThan(RedisKey key, long length)
        {
            return new LengthCondition(key, RedisType.Hash, -1, length);
        }

        /// <summary>
        /// Enforces that the given string length is a certain value
        /// </summary>
        public static Condition StringLengthEqual(RedisKey key, long length)
        {
            return new LengthCondition(key, RedisType.String, 0, length);
        }
        
        /// <summary>
        /// Enforces that the given string length is less than a certain value
        /// </summary>
        public static Condition StringLengthLessThan(RedisKey key, long length)
        {
            return new LengthCondition(key, RedisType.String, 1, length);
        }
        
        /// <summary>
        /// Enforces that the given string length is greater than a certain value
        /// </summary>
        public static Condition StringLengthGreaterThan(RedisKey key, long length)
        {
            return new LengthCondition(key, RedisType.String, -1, length);
        }
        
        /// <summary>
        /// Enforces that the given list length is a certain value
        /// </summary>
        public static Condition ListLengthEqual(RedisKey key, long length)
        {
            return new LengthCondition(key, RedisType.List, 0, length);
        }
        
        /// <summary>
        /// Enforces that the given list length is less than a certain value
        /// </summary>
        public static Condition ListLengthLessThan(RedisKey key, long length)
        {
            return new LengthCondition(key, RedisType.List, 1, length);
        }
        
        /// <summary>
        /// Enforces that the given list length is greater than a certain value
        /// </summary>
        public static Condition ListLengthGreaterThan(RedisKey key, long length)
        {
            return new LengthCondition(key, RedisType.List, -1, length);
        }
        
        /// <summary>
        /// Enforces that the given set cardinality is a certain value
        /// </summary>
        public static Condition SetLengthEqual(RedisKey key, long length)
        {
            return new LengthCondition(key, RedisType.Set, 0, length);
        }
        
        /// <summary>
        /// Enforces that the given set cardinality is less than a certain value
        /// </summary>
        public static Condition SetLengthLessThan(RedisKey key, long length)
        {
            return new LengthCondition(key, RedisType.Set, 1, length);
        }
        
        /// <summary>
        /// Enforces that the given set cardinality is greater than a certain value
        /// </summary>
        public static Condition SetLengthGreaterThan(RedisKey key, long length)
        {
            return new LengthCondition(key, RedisType.Set, -1, length);
        }
                
        /// <summary>
        /// Enforces that the given sorted set cardinality is a certain value
        /// </summary>
        public static Condition SortedSetLengthEqual(RedisKey key, long length)
        {
            return new LengthCondition(key, RedisType.SortedSet, 0, length);
        }
        
        /// <summary>
        /// Enforces that the given sorted set cardinality is less than a certain value
        /// </summary>
        public static Condition SortedSetLengthLessThan(RedisKey key, long length)
        {
            return new LengthCondition(key, RedisType.SortedSet, 1, length);
        }
        
        /// <summary>
        /// Enforces that the given sorted set cardinality is greater than a certain value
        /// </summary>
        public static Condition SortedSetLengthGreaterThan(RedisKey key, long length)
        {
            return new LengthCondition(key, RedisType.SortedSet, -1, length);
        }

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
                var msg = message as ConditionMessage;
                var condition = msg?.Condition;
                bool final;
                if (condition != null && condition.TryValidate(result, out final))
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
                    this.Condition = condition;
                    this.value = value; // note no assert here
                }

                internal override void WriteImpl(PhysicalConnection physical)
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
                        physical.Write(value);
                    }
                }
            }
        }

        internal class ExistsCondition : Condition
        {
            private readonly bool expectedResult;
            private readonly RedisValue hashField;
            private readonly RedisKey key;

            internal override Condition MapKeys(Func<RedisKey,RedisKey> map)
            {
                return new ExistsCondition(map(key), hashField, expectedResult);
            }
            public ExistsCondition(RedisKey key, RedisValue hashField, bool expectedResult)
            {
                if (key.IsNull) throw new ArgumentException("key");
                this.key = key;
                this.hashField = hashField;
                this.expectedResult = expectedResult;
            }

            public override string ToString()
            {
                return (hashField.IsNull ? key.ToString() : ((string)key) + " > " + hashField)
                    + (expectedResult ? " exists" : " does not exists");
            }

            internal override void CheckCommands(CommandMap commandMap)
            {
                commandMap.AssertAvailable(hashField.IsNull ? RedisCommand.EXISTS : RedisCommand.HEXISTS);
            }

            internal override IEnumerable<Message> CreateMessages(int db, ResultBox resultBox)
            {
                yield return Message.Create(db, CommandFlags.None, RedisCommand.WATCH, key);

                var cmd = hashField.IsNull ? RedisCommand.EXISTS : RedisCommand.HEXISTS;
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

        internal class EqualsCondition : Condition
        {

            internal override Condition MapKeys(Func<RedisKey,RedisKey> map)
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
            internal override Condition MapKeys(Func<RedisKey,RedisKey> map)
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
            internal override Condition MapKeys(Func<RedisKey,RedisKey> map)
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
                switch (type) {
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
                        value = parsed.IsInteger && (expectedLength.CompareTo((long) parsed) == compareToResult);
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
            this.Condition = condition;
            resultBox = ResultBox<bool>.Get(condition);
        }

        /// <summary>
        /// Indicates whether the condition was satisfied
        /// </summary>
        public bool WasSatisfied => wasSatisfied;

        internal IEnumerable<Message> CreateMessages(int db)
        {
            return Condition.CreateMessages(db, resultBox);
        }

        internal ResultBox<bool> GetBox() { return resultBox; }
        internal bool UnwrapBox()
        {
            if (resultBox != null)
            {
                Exception ex;
                bool val;
                ResultBox<bool>.UnwrapAndRecycle(resultBox, false, out val, out ex);
                resultBox = null;
                wasSatisfied = ex == null && val;
            }
            return wasSatisfied;
        }
    }
}
