using System;

namespace StackExchange.Redis
{
    /// <summary>
    /// The direction in which to sequence elements.
    /// </summary>
    public enum Order
    {
        /// <summary>
        /// Ordered from low values to high values.
        /// </summary>
        Ascending,
        /// <summary>
        /// Ordered from high values to low values.
        /// </summary>
        Descending,
    }

    internal static class OrderExtensions
    {
        internal static RedisValue ToLiteral(this Order order) => order switch
        {
            Order.Ascending => RedisLiterals.ASC,
            Order.Descending => RedisLiterals.DESC,
            _ => throw new ArgumentOutOfRangeException(nameof(order))
        };
    }
}
