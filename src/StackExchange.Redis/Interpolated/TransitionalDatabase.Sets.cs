using System;
using System.Threading.Tasks;

namespace StackExchange.Redis.Interpolated
{
    /// <summary>
    /// The set commands, where they have moved to the RESP context surface.
    /// </summary>
    /// <remarks>
    /// The group where the old surface's fixed-arity twins disappear entirely: <c>SetCombine</c> and
    /// <c>SetCombineAndStore</c> each have a <c>(first, second)</c> overload that existed only because
    /// building a variadic message used to be work. Both now unpack into the one group method.
    /// </remarks>
    internal sealed partial class TransitionalDatabase
    {
        /// <inheritdoc/>
        public bool SetAdd(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Sets.AddAsync(key, value, flags));

        /// <inheritdoc/>
        public Task<bool> SetAddAsync(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
            => Context.Sets.AddAsync(key, value, flags).AsTask();

        /// <inheritdoc/>
        public long SetAdd(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Sets.AddAsync(key, Required(values, nameof(values)), flags));

        /// <inheritdoc/>
        public Task<long> SetAddAsync(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None)
            => Context.Sets.AddAsync(key, Required(values, nameof(values)), flags).AsTask();

        /// <inheritdoc/>
        public bool SetRemove(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Sets.RemoveAsync(key, value, flags));

        /// <inheritdoc/>
        public Task<bool> SetRemoveAsync(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
            => Context.Sets.RemoveAsync(key, value, flags).AsTask();

        /// <inheritdoc/>
        public long SetRemove(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Sets.RemoveAsync(key, Required(values, nameof(values)), flags));

        /// <inheritdoc/>
        public Task<long> SetRemoveAsync(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None)
            => Context.Sets.RemoveAsync(key, Required(values, nameof(values)), flags).AsTask();

        /// <inheritdoc/>
        public bool SetContains(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Sets.ContainsAsync(key, value, flags));

        /// <inheritdoc/>
        public Task<bool> SetContainsAsync(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
            => Context.Sets.ContainsAsync(key, value, flags).AsTask();

        /// <inheritdoc/>
        public bool[] SetContains(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Sets.ContainsArray(key, Required(values, nameof(values)), flags));

        /// <inheritdoc/>
        public Task<bool[]> SetContainsAsync(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None)
            => Context.Sets.ContainsArray(key, Required(values, nameof(values)), flags).AsTask();

        /// <inheritdoc/>
        public long SetLength(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Sets.LengthAsync(key, flags));

        /// <inheritdoc/>
        public Task<long> SetLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Context.Sets.LengthAsync(key, flags).AsTask();

        /// <inheritdoc/>
        public RedisValue[] SetMembers(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Sets.MembersArray(key, flags));

        /// <inheritdoc/>
        public Task<RedisValue[]> SetMembersAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Context.Sets.MembersArray(key, flags).AsTask();

        /// <inheritdoc/>
        public bool SetMove(RedisKey source, RedisKey destination, RedisValue value, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Sets.MoveAsync(source, destination, value, flags));

        /// <inheritdoc/>
        public Task<bool> SetMoveAsync(RedisKey source, RedisKey destination, RedisValue value, CommandFlags flags = CommandFlags.None)
            => Context.Sets.MoveAsync(source, destination, value, flags).AsTask();

        /// <inheritdoc/>
        public RedisValue SetPop(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Sets.PopAsync(key, flags));

        /// <inheritdoc/>
        public Task<RedisValue> SetPopAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Context.Sets.PopAsync(key, flags).AsTask();

        /// <inheritdoc/>
        public RedisValue[] SetPop(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Sets.PopArray(key, count, flags));

        /// <inheritdoc/>
        public Task<RedisValue[]> SetPopAsync(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
            => Context.Sets.PopArray(key, count, flags).AsTask();

        /// <inheritdoc/>
        public RedisValue SetRandomMember(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Sets.RandomMemberAsync(key, flags));

        /// <inheritdoc/>
        public Task<RedisValue> SetRandomMemberAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Context.Sets.RandomMemberAsync(key, flags).AsTask();

        /// <inheritdoc/>
        public RedisValue[] SetRandomMembers(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Sets.RandomMembersArray(key, count, flags));

        /// <inheritdoc/>
        public Task<RedisValue[]> SetRandomMembersAsync(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
            => Context.Sets.RandomMembersArray(key, count, flags).AsTask();

        // the (first, second) overloads are the old spelling of a two-key run; unpacking them here is the
        // whole of the difference, and a null `second` is how that surface says "just the one"

        /// <inheritdoc/>
        public RedisValue[] SetCombine(SetOperation operation, RedisKey first, RedisKey second, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Sets.CombineArray(operation, Pair(first, second), flags));

        /// <inheritdoc/>
        public Task<RedisValue[]> SetCombineAsync(SetOperation operation, RedisKey first, RedisKey second, CommandFlags flags = CommandFlags.None)
            => Context.Sets.CombineArray(operation, Pair(first, second), flags).AsTask();

        /// <inheritdoc/>
        public RedisValue[] SetCombine(SetOperation operation, RedisKey[] keys, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Sets.CombineArray(operation, Required(keys, nameof(keys)), flags));

        /// <inheritdoc/>
        public Task<RedisValue[]> SetCombineAsync(SetOperation operation, RedisKey[] keys, CommandFlags flags = CommandFlags.None)
            => Context.Sets.CombineArray(operation, Required(keys, nameof(keys)), flags).AsTask();

        /// <inheritdoc/>
        public long SetCombineAndStore(SetOperation operation, RedisKey destination, RedisKey first, RedisKey second, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Sets.CombineAndStoreAsync(operation, destination, Pair(first, second), flags));

        /// <inheritdoc/>
        public Task<long> SetCombineAndStoreAsync(SetOperation operation, RedisKey destination, RedisKey first, RedisKey second, CommandFlags flags = CommandFlags.None)
            => Context.Sets.CombineAndStoreAsync(operation, destination, Pair(first, second), flags).AsTask();

        /// <inheritdoc/>
        public long SetCombineAndStore(SetOperation operation, RedisKey destination, RedisKey[] keys, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Sets.CombineAndStoreAsync(operation, destination, Required(keys, nameof(keys)), flags));

        /// <inheritdoc/>
        public Task<long> SetCombineAndStoreAsync(SetOperation operation, RedisKey destination, RedisKey[] keys, CommandFlags flags = CommandFlags.None)
            => Context.Sets.CombineAndStoreAsync(operation, destination, Required(keys, nameof(keys)), flags).AsTask();

        /// <inheritdoc/>
        public long SetIntersectionLength(RedisKey[] keys, long limit = 0, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Sets.CombineLengthAsync(SetOperation.Intersect, Required(keys, nameof(keys)), limit, approximate: false, flags));

        /// <inheritdoc/>
        public Task<long> SetIntersectionLengthAsync(RedisKey[] keys, long limit = 0, CommandFlags flags = CommandFlags.None)
            => Context.Sets.CombineLengthAsync(SetOperation.Intersect, Required(keys, nameof(keys)), limit, approximate: false, flags).AsTask();

        /// <inheritdoc/>
        public long SetCombineLength(SetOperation operation, RedisKey[] keys, long limit = 0, bool approximate = false, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Sets.CombineLengthAsync(operation, Required(keys, nameof(keys)), limit, approximate, flags));

        /// <inheritdoc/>
        public Task<long> SetCombineLengthAsync(SetOperation operation, RedisKey[] keys, long limit = 0, bool approximate = false, CommandFlags flags = CommandFlags.None)
            => Context.Sets.CombineLengthAsync(operation, Required(keys, nameof(keys)), limit, approximate, flags).AsTask();

        /// <summary>
        /// The old <c>(first, second)</c> shape as a run of keys; a default <c>second</c> means one key.
        /// </summary>
        private static RedisKey[] Pair(in RedisKey first, in RedisKey second)
            => second.IsNull ? [first] : [first, second];
    }
}
