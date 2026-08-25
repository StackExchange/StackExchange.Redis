namespace StackExchange.Redis;

/// <summary>
/// Well-known index values for the bitmap operations that take a start/end range.
/// </summary>
public static class StringIndex
{
    /// <summary>
    /// Indicates that no end index should be sent to the server, leaving the range open-ended.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Valid only as the <c>end</c> of <see cref="IDatabase.StringBitPosition(RedisKey, bool, long, long, StringIndexType, CommandFlags)"/>
    /// (and the async equivalent), and only in combination with <see cref="StringIndexType.Byte"/>: <c>BITPOS</c>
    /// accepts a bit/byte index type only after an explicit end.
    /// </para>
    /// <para>
    /// This is not interchangeable with <c>-1</c>. When searching for a clear bit, an explicit end restricts the
    /// search to the string itself, so an all-set string yields -1; an open-ended range instead reports the first
    /// bit beyond the end of the string.
    /// </para>
    /// </remarks>
    public const long Unbounded = long.MinValue;
}
