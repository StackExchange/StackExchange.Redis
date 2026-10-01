using System;
using System.Threading.Tasks;

namespace StackExchange.Redis
{
    /// <summary>
    /// The bitmap commands, where they have moved to the RESP context surface.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The old surface calls these <c>StringBitXxx</c> and the new one groups them under
    /// <see cref="RespBitmaps"/>; this file is where the two names meet, and is the reason regrouping them
    /// costs existing callers nothing.
    /// </para>
    /// <para>
    /// Note what is <i>not</i> here: <c>StringBitOperation(operation, destination, first, second)</c> is
    /// implemented in terms of the multi-key form, because the group has only the multi-key form. The
    /// default <c>second</c> is the old surface's way of spelling a unary operation, and unpacking it here
    /// is the whole of the difference.
    /// </para>
    /// </remarks>
    internal sealed partial class TransitionalDatabase
    {
        /// <inheritdoc/>
        public bool StringGetBit(RedisKey key, long offset, CommandFlags flags = CommandFlags.None)
            => Wait(_inner.Bitmaps.GetAsync(key, offset, flags));

        /// <inheritdoc/>
        public Task<bool> StringGetBitAsync(RedisKey key, long offset, CommandFlags flags = CommandFlags.None)
            => _inner.Bitmaps.GetAsync(key, offset, flags).AsTask();

        /// <inheritdoc/>
        public bool StringSetBit(RedisKey key, long offset, bool bit, CommandFlags flags = CommandFlags.None)
            => Wait(_inner.Bitmaps.SetAsync(key, offset, bit, flags));

        /// <inheritdoc/>
        public Task<bool> StringSetBitAsync(RedisKey key, long offset, bool bit, CommandFlags flags = CommandFlags.None)
            => _inner.Bitmaps.SetAsync(key, offset, bit, flags).AsTask();

        /// <inheritdoc/>
        public long StringBitCount(RedisKey key, long start, long end, CommandFlags flags)
            => StringBitCount(key, start, end, StringIndexType.Byte, flags);

        /// <inheritdoc/>
        public Task<long> StringBitCountAsync(RedisKey key, long start, long end, CommandFlags flags)
            => StringBitCountAsync(key, start, end, StringIndexType.Byte, flags);

        /// <inheritdoc/>
        public long StringBitCount(RedisKey key, long start = 0, long end = -1, StringIndexType indexType = StringIndexType.Byte, CommandFlags flags = CommandFlags.None)
            => Wait(_inner.Bitmaps.CountAsync(key, start, end, indexType, flags));

        /// <inheritdoc/>
        public Task<long> StringBitCountAsync(RedisKey key, long start = 0, long end = -1, StringIndexType indexType = StringIndexType.Byte, CommandFlags flags = CommandFlags.None)
            => _inner.Bitmaps.CountAsync(key, start, end, indexType, flags).AsTask();

        /// <inheritdoc/>
        public long StringBitPosition(RedisKey key, bool bit, long start, long end, CommandFlags flags)
            => StringBitPosition(key, bit, start, end, StringIndexType.Byte, flags);

        /// <inheritdoc/>
        public Task<long> StringBitPositionAsync(RedisKey key, bool bit, long start, long end, CommandFlags flags)
            => StringBitPositionAsync(key, bit, start, end, StringIndexType.Byte, flags);

        /// <inheritdoc/>
        public long StringBitPosition(RedisKey key, bool bit, long start = 0, long end = -1, StringIndexType indexType = StringIndexType.Byte, CommandFlags flags = CommandFlags.None)
            => Wait(_inner.Bitmaps.PositionAsync(key, bit, start, end, indexType, flags));

        /// <inheritdoc/>
        public Task<long> StringBitPositionAsync(RedisKey key, bool bit, long start = 0, long end = -1, StringIndexType indexType = StringIndexType.Byte, CommandFlags flags = CommandFlags.None)
            => _inner.Bitmaps.PositionAsync(key, bit, start, end, indexType, flags).AsTask();

        /// <inheritdoc/>
        public long StringBitOperation(Bitwise operation, RedisKey destination, RedisKey first, RedisKey second = default, CommandFlags flags = CommandFlags.None)
            => Wait(_inner.Bitmaps.OperationAsync(operation, destination, Sources(operation, first, second), flags));

        /// <inheritdoc/>
        public Task<long> StringBitOperationAsync(Bitwise operation, RedisKey destination, RedisKey first, RedisKey second = default, CommandFlags flags = CommandFlags.None)
            => _inner.Bitmaps.OperationAsync(operation, destination, Sources(operation, first, second), flags).AsTask();

        /// <inheritdoc/>
        public long StringBitOperation(Bitwise operation, RedisKey destination, RedisKey[] keys, CommandFlags flags = CommandFlags.None)
            => Wait(_inner.Bitmaps.OperationAsync(operation, destination, Required(keys, nameof(keys)), flags));

        /// <inheritdoc/>
        public Task<long> StringBitOperationAsync(Bitwise operation, RedisKey destination, RedisKey[] keys, CommandFlags flags = CommandFlags.None)
            => _inner.Bitmaps.OperationAsync(operation, destination, Required(keys, nameof(keys)), flags).AsTask();

        /// <inheritdoc/>
        public long? StringBitField(RedisKey key, BitFieldOperation operation, CommandFlags flags = CommandFlags.None)
            => Wait(_inner.Bitmaps.FieldAsync(key, operation, flags));

        /// <inheritdoc/>
        public Task<long?> StringBitFieldAsync(RedisKey key, BitFieldOperation operation, CommandFlags flags = CommandFlags.None)
            => _inner.Bitmaps.FieldAsync(key, operation, flags).AsTask();

        /// <inheritdoc/>
        public Lease<long?> StringBitField(RedisKey key, ReadOnlyMemory<BitFieldOperation> operations, CommandFlags flags = CommandFlags.None)
            => Wait(_inner.Bitmaps.FieldWritableLease(key, operations.Span, flags));

        /// <inheritdoc/>
        public Task<Lease<long?>> StringBitFieldAsync(RedisKey key, ReadOnlyMemory<BitFieldOperation> operations, CommandFlags flags = CommandFlags.None)
            => _inner.Bitmaps.FieldWritableLease(key, operations.Span, flags).AsTask();

        /// <summary>
        /// The old <c>(first, second)</c> shape as a run of source keys: a default <c>second</c>, or a NOT,
        /// means unary.
        /// </summary>
        /// <remarks>
        /// <c>operation == Bitwise.Not</c> is part of the test in <c>RedisDatabase</c> too, and it is not
        /// redundant: a caller who passes a second key to a NOT gets it ignored there, and would get an
        /// argument exception from the group. Keeping the old behaviour for the old spelling is the point
        /// of an adapter.
        /// </remarks>
        private static RedisKey[] Sources(Bitwise operation, in RedisKey first, in RedisKey second)
            => second.IsNull || operation == Bitwise.Not ? [first] : [first, second];
    }
}
