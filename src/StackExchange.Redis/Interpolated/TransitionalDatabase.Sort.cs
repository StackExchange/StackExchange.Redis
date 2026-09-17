using System;
using System.Threading.Tasks;

namespace StackExchange.Redis
{
    /// <summary>
    /// <c>SORT</c>, where it has moved to the RESP context surface.
    /// </summary>
    /// <remarks>
    /// Four members and one renderer. The group is <c>Keys</c> rather than a group of its own, because
    /// <c>SORT</c> takes a list, a set or a sorted set and Redis files it under the generic commands.
    /// </remarks>
    internal sealed partial class TransitionalDatabase
    {
        /// <inheritdoc/>
        public RedisValue[] Sort(RedisKey key, long skip = 0, long take = -1, Order order = Order.Ascending, SortType sortType = SortType.Numeric, RedisValue by = default, RedisValue[]? get = null, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Keys.SortArray(key, skip, take, order, sortType, by, get, flags));

        /// <inheritdoc/>
        public Task<RedisValue[]> SortAsync(RedisKey key, long skip = 0, long take = -1, Order order = Order.Ascending, SortType sortType = SortType.Numeric, RedisValue by = default, RedisValue[]? get = null, CommandFlags flags = CommandFlags.None)
            => Context.Keys.SortArray(key, skip, take, order, sortType, by, get, flags).AsTask();

        /// <inheritdoc/>
        public long SortAndStore(RedisKey destination, RedisKey key, long skip = 0, long take = -1, Order order = Order.Ascending, SortType sortType = SortType.Numeric, RedisValue by = default, RedisValue[]? get = null, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Keys.SortAndStoreAsync(destination, key, skip, take, order, sortType, by, get, flags));

        /// <inheritdoc/>
        public Task<long> SortAndStoreAsync(RedisKey destination, RedisKey key, long skip = 0, long take = -1, Order order = Order.Ascending, SortType sortType = SortType.Numeric, RedisValue by = default, RedisValue[]? get = null, CommandFlags flags = CommandFlags.None)
            => Context.Keys.SortAndStoreAsync(destination, key, skip, take, order, sortType, by, get, flags).AsTask();
    }
}
