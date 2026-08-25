using System;

namespace StackExchange.Redis;

internal partial class RedisDatabase
{
    /// <summary>
    /// <c>BITFIELD</c>/<c>BITFIELD_RO</c>: the key, then one clause per operation.
    /// </summary>
    /// <remarks>
    /// The <c>OVERFLOW</c> token is sticky server-side, and is neither reset nor consumed by a
    /// <c>GET</c>, so it is written only when the required mode changes - and never for a leading run
    /// of <see cref="BitFieldOverflow.Wrap"/>, which is the server's own default.
    /// </remarks>
    internal abstract class BitFieldMessageBase : Message.CommandKeyBase
    {
        protected BitFieldMessageBase(int db, CommandFlags flags, RedisCommand command, in RedisKey key)
            : base(db, flags, command, key)
        {
        }

        /// <summary>
        /// Counts the arguments needed by a set of operations, including the key, and rejects anything
        /// that cannot be written - while we can still throw at the caller rather than mid-write.
        /// </summary>
        protected static int CountArgs(ReadOnlySpan<BitFieldOperation> operations, string paramName)
        {
            int count = 1; // the key
            var overflow = BitFieldOverflow.Wrap;
            foreach (ref readonly var op in operations)
            {
                switch (op.Kind)
                {
                    case BitFieldOperation.OperationKind.Get:
                        count += 3; // GET, encoding, offset
                        break;
                    case BitFieldOperation.OperationKind.Set:
                    case BitFieldOperation.OperationKind.IncrementBy:
                        if (op.Overflow != overflow)
                        {
                            overflow = op.Overflow;
                            count += 2; // OVERFLOW, mode
                        }

                        count += 4; // SET/INCRBY, encoding, offset, value
                        break;
                    default:
                        throw new ArgumentException($"A default {nameof(BitFieldOperation)} is not a valid operation.", paramName);
                }
            }

            return count;
        }

        protected static void WriteOperations(in MessageWriter writer, ReadOnlySpan<BitFieldOperation> operations)
        {
            // enough for the widest thing either part writes: a framed encoding ("$3\r\ni64\r\n", 9) or
            // an element offset payload ('#' plus an int64, 21)
            Span<byte> scratch = stackalloc byte[Format.MaxInt64TextLen + 1];
            var overflow = BitFieldOverflow.Wrap;
            foreach (ref readonly var op in operations)
            {
                if (op.Kind != BitFieldOperation.OperationKind.Get && op.Overflow != overflow)
                {
                    overflow = op.Overflow;
                    writer.WriteRaw("$8\r\nOVERFLOW\r\n"u8);
                    switch (overflow)
                    {
                        case BitFieldOverflow.Saturate:
                            writer.WriteRaw("$3\r\nSAT\r\n"u8);
                            break;
                        case BitFieldOverflow.Fail:
                            writer.WriteRaw("$4\r\nFAIL\r\n"u8);
                            break;
                        default:
                            writer.WriteRaw("$4\r\nWRAP\r\n"u8);
                            break;
                    }
                }

                switch (op.Kind)
                {
                    case BitFieldOperation.OperationKind.Get:
                        writer.WriteRaw("$3\r\nGET\r\n"u8);
                        break;
                    case BitFieldOperation.OperationKind.Set:
                        writer.WriteRaw("$3\r\nSET\r\n"u8);
                        break;
                    default:
                        writer.WriteRaw("$6\r\nINCRBY\r\n"u8);
                        break;
                }

                op.Encoding.Write(in writer, scratch);
                op.Offset.Write(in writer, scratch);
                if (op.Kind != BitFieldOperation.OperationKind.Get)
                {
                    writer.WriteBulkString(op.Value);
                }
            }
        }
    }

    /// <inheritdoc cref="BitFieldMessageBase"/>
    internal sealed class BitFieldMessage : BitFieldMessageBase
    {
        private readonly ReadOnlyMemory<BitFieldOperation> _operations;
        private readonly int _argCount;

        public BitFieldMessage(int db, CommandFlags flags, RedisCommand command, in RedisKey key, ReadOnlyMemory<BitFieldOperation> operations)
            : base(db, flags, command, key)
        {
            _operations = operations;
            _argCount = CountArgs(operations.Span, nameof(operations));
        }

        protected override void WriteImpl(in MessageWriter writer)
        {
            writer.WriteHeader(Command, _argCount);
            writer.Write(Key);
            WriteOperations(in writer, _operations.Span);
        }

        public override int ArgCount => _argCount;
    }

    /// <inheritdoc cref="BitFieldMessageBase"/>
    internal sealed class BitFieldSingleMessage : BitFieldMessageBase
    {
        private readonly BitFieldOperation _operation;
        private readonly int _argCount;

        public BitFieldSingleMessage(int db, CommandFlags flags, RedisCommand command, in RedisKey key, in BitFieldOperation operation)
            : base(db, flags, command, key)
        {
            _operation = operation;
            _argCount = CountArgs([operation], nameof(operation));
        }

        protected override void WriteImpl(in MessageWriter writer)
        {
            writer.WriteHeader(Command, _argCount);
            writer.Write(Key);
            WriteOperations(in writer, [_operation]);
        }

        public override int ArgCount => _argCount;
    }
}
