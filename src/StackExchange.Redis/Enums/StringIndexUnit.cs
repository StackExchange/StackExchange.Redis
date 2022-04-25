using System;

namespace StackExchange.Redis;

/// <summary>
/// Indicates if we speciding the index in bits or in bytes.
/// </summary>
public enum StringIndexUnit
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

internal static class StringIndexUnitExtensions
{
    internal static RedisValue ToLiteral(this StringIndexUnit unit) => unit switch
    {
        StringIndexUnit.Bit => RedisLiterals.BIT,
        StringIndexUnit.Byte => RedisLiterals.BYTE,
        _ => throw new ArgumentOutOfRangeException(nameof(unit))
    };
}
