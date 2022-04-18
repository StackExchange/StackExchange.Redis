using System;

namespace StackExchange.Redis;

/// <summary>
/// Represents an integers signedness
/// </summary>
public enum Signedness
{
    /// <summary>
    /// An integer with no sign bit.
    /// </summary>
    Unsigned,
    /// <summary>
    /// An integer with a sign bit.
    /// </summary>
    Signed
}

internal static class SignednessExtensions
{
    internal static char SignChar(this Signedness sign) => sign switch
    {
        Signedness.Signed => 'i',
        Signedness.Unsigned => 'u',
        _ => throw new ArgumentOutOfRangeException(nameof(sign))
    };
}
