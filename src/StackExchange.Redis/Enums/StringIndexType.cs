using System;

namespace StackExchange.Redis;

/// <summary>
/// Indicates if we index into a string based on bits or bytes.
/// </summary>
public enum StringIndexType
{
    /// <summary>
    /// Indicates the index is the number of bytes into a string.
    /// </summary>
    Byte,
    /// <summary>
    /// Indicates the index is the number of bits into a string.
    /// </summary>
    Bit,
}

internal static class StringIndexTypeExtensions
{
    internal static RedisValue ToLiteral(this StringIndexType indexType) => indexType switch
    {
        StringIndexType.Bit => RedisLiterals.BIT,
        StringIndexType.Byte => RedisLiterals.BYTE,
        _ => throw new ArgumentOutOfRangeException(nameof(indexType))
    };
}
