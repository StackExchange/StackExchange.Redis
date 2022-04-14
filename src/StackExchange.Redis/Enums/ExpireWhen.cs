using System;

namespace StackExchange.Redis
{
    /// <summary>
    /// Specifies what side of the list to refer to.
    /// </summary>
    public enum ExpiryWhen
    {
        /// <summary>
        /// Set expiry whether or not there is an existing expiry.
        /// </summary>
        Always,
        /// <summary>
        /// Set expiry only when the new expiry is greater than current one
        /// </summary>
        GreaterThanCurrentExpiry,
        /// <summary>
        /// Set expiry only when the key has an existing expiry
        /// </summary>
        HasExpiry,
        /// <summary>
        /// Set expiry only when the key has no expiry
        /// </summary>
        HasNoExpiry,
        /// <summary>
        /// Set expiry only when the new expiry is less than current one
        /// </summary>
        LessThanCurrentExpiry,
    }

    internal static class ExpiryOptionExtensions
    {
        public static RedisValue ToLiteral(this ExpiryWhen op) => op switch
        {
            ExpiryWhen.HasNoExpiry => RedisLiterals.NX,
            ExpiryWhen.HasExpiry => RedisLiterals.XX,
            ExpiryWhen.GreaterThanCurrentExpiry => RedisLiterals.GT,
            ExpiryWhen.LessThanCurrentExpiry => RedisLiterals.LT,
            _ => throw new ArgumentOutOfRangeException(nameof(op))
        };
    }
}
