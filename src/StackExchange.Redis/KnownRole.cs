using System;
using RESPite;

namespace StackExchange.Redis;

/// <summary>
/// Known role values used during auto-configuration parsing.
/// </summary>
internal enum KnownRole
{
    /// <summary>
    /// Unknown or unrecognized role.
    /// </summary>
    [AsciiHash("")]
    None = 0,

    [AsciiHash("primary")]
    Primary,

    [AsciiHash("master")]
    Master,

    [AsciiHash("replica")]
    Replica,

    [AsciiHash("slave")]
    Slave,
}

/// <summary>
/// Metadata and parsing methods for <see cref="KnownRole"/>.
/// </summary>
internal static partial class KnownRoleMetadata
{
    [AsciiHash]
    private static partial bool TryParseCore(ReadOnlySpan<byte> value, out KnownRole role);

    [AsciiHash]
    private static partial bool TryParseCore(ReadOnlySpan<char> value, out KnownRole role);

    internal static bool TryParse(ReadOnlySpan<byte> value, out bool isReplica)
    {
        if (!TryParseCore(value, out var role))
        {
            isReplica = false;
            return false;
        }

        isReplica = role is KnownRole.Replica or KnownRole.Slave;
        return true;
    }

    internal static bool TryParse(ReadOnlySpan<char> value, out bool isReplica)
    {
        if (!TryParseCore(value, out var role))
        {
            isReplica = false;
            return false;
        }

        isReplica = role is KnownRole.Replica or KnownRole.Slave;
        return true;
    }

    internal static bool TryParse(string? value, out bool isReplica)
    {
        if (value is not null) return TryParse(value.AsSpan(), out isReplica);
        isReplica = false;
        return false;
    }
}
