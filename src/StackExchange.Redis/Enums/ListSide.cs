using System;

namespace StackExchange.Redis
{
    /// <summary>
    /// Specifies what side of the list to refer to.
    /// </summary>
    public enum ListSide
    {
        /// <summary>
        /// The head of the list.
        /// </summary>
        Left,
        /// <summary>
        /// The tail of the list.
        /// </summary>
        Right,
    }

    internal static class ListSideExtensions
    {
        internal static RedisValue ToLiteral(this ListSide side) => side switch
        {
            ListSide.Left => RedisLiterals.LEFT,
            ListSide.Right => RedisLiterals.RIGHT,
            _ => throw new ArgumentOutOfRangeException(nameof(side))
        };
    }
}
