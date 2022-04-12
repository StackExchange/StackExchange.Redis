using System;

namespace StackExchange.Redis
{
    /// <summary>
    /// Specifies what side of the list to refer to.
    /// </summary>
    public enum ExpiryOption
    {
        /// <summary>
        /// Set expiry only when the key has no expiry
        /// </summary>
        NX,
        /// <summary>
        /// Set expiry only when the key has an existing expiry
        /// </summary>
        XX,
        /// <summary>
        /// Set expiry only when the new expiry is greater than current one
        /// </summary>
        GT,
        /// <summary>
        /// Set expiry only when the new expiry is less than current one
        /// </summary>
        LT,
    }

    internal static class ExpiryOptionExtensions
    {
        public static RedisValue ToLiteral(this ExpiryOption side) => side switch
        {
            ExpiryOption.NX => RedisLiterals.NX,
            ExpiryOption.XX => RedisLiterals.XX,
            ExpiryOption.GT => RedisLiterals.GT,
            ExpiryOption.LT => RedisLiterals.LT,
            _ => throw new ArgumentOutOfRangeException(nameof(side))
        };
    }
}
