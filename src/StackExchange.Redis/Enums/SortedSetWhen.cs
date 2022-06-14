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
        internal static uint CountBits(this SortedSetWhen when)
        {
            uint v = (uint)when;
            v -= ((v >> 1) & 0x55555555); // reuse input as temporary
            v = (v & 0x33333333) + ((v >> 2) & 0x33333333); // temp
            uint c = ((v + (v >> 4) & 0xF0F0F0F) * 0x1010101) >> 24; // count
            return c;
        }

        internal static SortedSetWhen Parse(When when)=> when switch
        {
            When.Always => SortedSetWhen.Always,
            When.Exists => SortedSetWhen.Exists,
            When.NotExists => SortedSetWhen.NotExists,
            _ => throw new ArgumentOutOfRangeException(nameof(when))
        };
    }
}
