using System;

namespace StackExchange.Redis;

/// <summary>
/// Defines the overflow behavior of a BITFIELD command.
/// </summary>
public enum BitfieldOverflowHandling
{
    /// <summary>
    /// Wraps around to the most negative value of signed integers, or zero for unsigned integers
    /// </summary>
    Wrap,
    /// <summary>
    /// Uses saturation arithmetic, stopping at the highest possible value for overflows, and the lowest possible value for underflows.
    /// </summary>
    Saturate,
    /// <summary>
    /// If an overflow is encountered, associated subcommand fails, and the result will be NULL.
    /// </summary>
    Fail,
}

internal static class BitfieldOverflowHandlingExtensions
{
    internal static RedisValue AsRedisValue(this BitfieldOverflowHandling handling) => handling switch
    {
        BitfieldOverflowHandling.Fail => RedisLiterals.FAIL,
        BitfieldOverflowHandling.Saturate => RedisLiterals.SAT,
        BitfieldOverflowHandling.Wrap => RedisLiterals.WRAP,
        _ => throw new ArgumentOutOfRangeException(nameof(handling))
    };
}
