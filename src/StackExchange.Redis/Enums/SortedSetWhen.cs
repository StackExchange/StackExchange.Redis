using System;

namespace StackExchange.Redis
{
    /// <summary>
    /// Indicates when this operation should be performed (only some variations are legal in a given context).
    /// </summary>
    [Flags]
    public enum SortedSetWhen
    {
        /// <summary>
        /// The operation won't be prevented.
        /// </summary>
        Always = 0,

        /// <summary>
        /// The operation should only occur when there is an existing value.
        /// </summary>
        Exists = 1 << 0,

        /// <summary>
        /// The operation should only occur when the new score is greater than the current score.
        /// </summary>
        GreaterThan = 1 << 1,

        /// <summary>
        /// The operation should only occur when the new score is less than the current score.
        /// </summary>
        LessThan = 1 << 2,

        /// <summary>
        /// The operation should only occur when there is not an existing value.
        /// </summary>
        NotExists = 1 << 3,
    }

    internal static class SortedSetWhenExtensions
    {
        internal static SortedSetWhen Parse(When when) => when switch
        {
            When.Always => SortedSetWhen.Always,
            When.Exists => SortedSetWhen.Exists,
            When.NotExists => SortedSetWhen.NotExists,
            _ => throw new ArgumentOutOfRangeException(nameof(when)),
        };
    }
}
