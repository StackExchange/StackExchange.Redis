using System;

namespace StackExchange.Redis;

/// <summary>
/// Indicates if we index with bit unit of byte unit.
/// </summary>
public enum StringIndexType
{
    /// <summary>
    /// Use bit
    /// </summary>
    Bit,
    /// <summary>
    /// Use byte
    /// </summary>
    Byte,
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
