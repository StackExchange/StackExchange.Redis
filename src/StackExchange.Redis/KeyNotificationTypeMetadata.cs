using System;
using RESPite;

namespace StackExchange.Redis;

/// <summary>
/// Metadata and parsing methods for KeyNotificationType.
/// </summary>
internal static partial class KeyNotificationTypeMetadata
{
    [AsciiHash]
    internal static partial bool TryParse(ReadOnlySpan<byte> value, out KeyNotificationType keyNotificationType);

    [AsciiHash]
    private static partial bool TryFormat(KeyNotificationType type, out ReadOnlySpan<byte> value);

    public static KeyNotificationType Parse(ReadOnlySpan<byte> value)
    {
        return TryParse(value, out var result) ? result : KeyNotificationType.Unknown;
    }

    internal static ReadOnlySpan<byte> GetRawBytes(KeyNotificationType type)
    {
        if (TryFormat(type, out var value)) return value;
        throw new ArgumentOutOfRangeException(nameof(type));
    }
}
