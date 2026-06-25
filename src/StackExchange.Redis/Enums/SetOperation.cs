using System;

namespace StackExchange.Redis
{
    /// <summary>
    /// Describes an algebraic set operation that can be performed to combine multiple sets.
    /// </summary>
    public enum SetOperation
    {
        /// <summary>
        /// Returns the members of the set resulting from the union of all the given sets.
        /// </summary>
        Union,

        /// <summary>
        /// Returns the members of the set resulting from the intersection of all the given sets.
        /// </summary>
        Intersect,

        /// <summary>
        /// Returns the members of the set resulting from the difference between the first set and all the successive sets.
        /// </summary>
        Difference,
    }

    internal static class SetOperationExtensions
    {
        internal static RedisCommand ToSetCommand(this SetOperation operation) => operation switch
        {
            SetOperation.Union => RedisCommand.SUNION,
            SetOperation.Intersect => RedisCommand.SINTER,
            SetOperation.Difference => RedisCommand.SDIFF,
            _ => OutOfRange(operation),
        };

        internal static RedisCommand ToSetStoreCommand(this SetOperation operation) => operation switch
        {
            SetOperation.Union => RedisCommand.SUNIONSTORE,
            SetOperation.Intersect => RedisCommand.SINTERSTORE,
            SetOperation.Difference => RedisCommand.SDIFFSTORE,
            _ => OutOfRange(operation),
        };

        internal static RedisCommand ToSortedSetCommand(this SetOperation operation) => operation switch
        {
            SetOperation.Union => RedisCommand.ZUNION,
            SetOperation.Intersect => RedisCommand.ZINTER,
            SetOperation.Difference => RedisCommand.ZDIFF,
            _ => OutOfRange(operation),
        };

        internal static RedisCommand ToSortedSetStoreCommand(this SetOperation operation) => operation switch
        {
            SetOperation.Union => RedisCommand.ZUNIONSTORE,
            SetOperation.Intersect => RedisCommand.ZINTERSTORE,
            SetOperation.Difference => RedisCommand.ZDIFFSTORE,
            _ => OutOfRange(operation),
        };

        private static RedisCommand OutOfRange(SetOperation operation) =>
            throw new ArgumentOutOfRangeException(nameof(operation), operation, null);
    }
}
