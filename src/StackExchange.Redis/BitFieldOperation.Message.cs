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
        /// Accumulates the arguments needed by one operation, tracking the sticky overflow mode;
        /// <see langword="false"/> if it cannot be written.
        /// </summary>
        protected static bool TryCountArgs(in BitFieldOperation operation, ref BitFieldOverflow overflow, ref int count)
        {
            switch (operation.Kind)
            {
                case BitFieldOperation.OperationKind.Get:
                    count += 3; // GET, encoding, offset
                    return true;
                case BitFieldOperation.OperationKind.Set:
                case BitFieldOperation.OperationKind.IncrementBy:
                    if (operation.Overflow != overflow)
                    {
                        overflow = operation.Overflow;
                        count += 2; // OVERFLOW, mode
                    }

                    count += 4; // SET/INCRBY, encoding, offset, value
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Counts the arguments needed by a set of operations, including the key;
        /// <see langword="false"/> if any of them cannot be written.
        /// </summary>
        protected static bool TryCountArgs(ReadOnlySpan<BitFieldOperation> operations, out int count)
        {
            count = 1; // the key
            var overflow = BitFieldOverflow.Wrap;
            foreach (ref readonly var op in operations)
            {
                if (!TryCountArgs(in op, ref overflow, ref count)) return false;
            }

            return true;
        }

        /// <summary>
        /// Counts the arguments, throwing at the caller - rather than mid-write - if the operations
        /// cannot be written.
        /// </summary>
        protected static int CountArgs(ReadOnlySpan<BitFieldOperation> operations, string paramName) =>
            TryCountArgs(operations, out var count) ? count : throw InvalidOperation(paramName);

        protected static ArgumentException InvalidOperation(string paramName) =>
            new($"A default {nameof(BitFieldOperation)} is not a valid operation.", paramName);

        protected static void WriteOperations(in MessageWriter writer, ReadOnlySpan<BitFieldOperation> operations)
        {
            // enough for the widest thing either part writes: a framed encoding ("$3\r\ni64\r\n", 9) or
            // an element offset payload ('#' plus an int64, 21)
            Span<byte> scratch = stackalloc byte[Format.MaxInt64TextLen + 1];
            var overflow = BitFieldOverflow.Wrap;
            foreach (ref readonly var op in operations)
            {
                WriteOperation(in writer, in op, ref overflow, scratch);
            }
        }

        /// <summary>
        /// Writes one operation, emitting the <c>OVERFLOW</c> token only when the sticky mode changes.
        /// </summary>
        protected static void WriteOperation(in MessageWriter writer, in BitFieldOperation operation, ref BitFieldOverflow overflow, Span<byte> scratch)
        {
            if (operation.Kind != BitFieldOperation.OperationKind.Get && operation.Overflow != overflow)
            {
                overflow = operation.Overflow;
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
                        // shape-neutral (the count comes from the transition, not the mode), so
                        // the server's own default is the safe answer here
                        writer.WriteRaw("$4\r\nWRAP\r\n"u8);
                        break;
                }
            }

            switch (operation.Kind)
            {
                case BitFieldOperation.OperationKind.Get:
                    writer.WriteRaw("$3\r\nGET\r\n"u8);
                    break;
                case BitFieldOperation.OperationKind.Set:
                    writer.WriteRaw("$3\r\nSET\r\n"u8);
                    break;
                case BitFieldOperation.OperationKind.IncrementBy:
                    writer.WriteRaw("$6\r\nINCRBY\r\n"u8);
                    break;
                default:
                    // unreachable: the callers check the operations before writing the header.
                    // Guessing here would be worse than failing - a wrong sub-command corrupts
                    // data, and one of the wrong arity corrupts the connection
                    throw new InvalidOperationException($"A default {nameof(BitFieldOperation)} is not a valid operation.");
            }

            operation.Encoding.Write(in writer, scratch);
            operation.Offset.Write(in writer, scratch);
            if (operation.Kind != BitFieldOperation.OperationKind.Get)
            {
                writer.WriteBulkString(operation.Value);
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
            // we alias the caller's memory rather than copying it, and here - unlike the messages that
            // hold a RedisValue[] - the *shape* depends on the contents, so a mutation between issue
            // and write would desync the frame. Re-check before the header goes out, so that a
            // violated contract fails with nothing written rather than corrupting the connection.
            // Note this becomes redundant once the write path serializes on the calling thread: with
            // no deferral there is no interval in which reasonable code could mutate the operations
            var operations = _operations.Span;
            if (!TryCountArgs(operations, out var count) || count != _argCount)
            {
                throw new InvalidOperationException($"The {nameof(BitFieldOperation)} values were modified after the command was issued.");
            }

            writer.WriteHeader(Command, _argCount);
            writer.Write(Key);
            WriteOperations(in writer, operations);
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

            // deliberately not `[operation]`: a collection expression of one element compiles to a
            // heap array on the target frameworks without the by-ref span constructor, which would
            // undo the point of holding the operation inline
            _argCount = 1; // the key
            var overflow = BitFieldOverflow.Wrap;
            if (!TryCountArgs(in operation, ref overflow, ref _argCount)) throw InvalidOperation(nameof(operation));
        }

        protected override void WriteImpl(in MessageWriter writer)
        {
            writer.WriteHeader(Command, _argCount);
            writer.Write(Key);

            Span<byte> scratch = stackalloc byte[Format.MaxInt64TextLen + 1];
            var overflow = BitFieldOverflow.Wrap;
            WriteOperation(in writer, in _operation, ref overflow, scratch);
        }

        public override int ArgCount => _argCount;
    }
}
