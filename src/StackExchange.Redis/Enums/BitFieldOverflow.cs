namespace StackExchange.Redis;

/// <summary>
/// How <c>BITFIELD</c> should behave when a <c>SET</c> or <c>INCRBY</c> operation would exceed the
/// range of the target <see cref="BitFieldEncoding"/>.
/// </summary>
/// <remarks><seealso href="https://redis.io/commands/bitfield"/></remarks>
public enum BitFieldOverflow
{
    /// <summary>
    /// Wrap around, using the usual two's-complement arithmetic; this is the server default.
    /// </summary>
    Wrap,

    /// <summary>
    /// Saturate at the minimum or maximum value of the encoding.
    /// </summary>
    Saturate,

    /// <summary>
    /// Perform no operation at all, reporting no value for the failed operation.
    /// </summary>
    Fail,
}
