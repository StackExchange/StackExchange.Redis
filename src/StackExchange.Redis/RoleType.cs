using System;
using RESPite;

namespace StackExchange.Redis;

/// <summary>
/// Redis server role types.
/// </summary>
// [AsciiHash(nameof(RoleTypeMetadata))]
internal enum RoleType
{
    /// <summary>
    /// Unknown or unrecognized role.
    /// </summary>
    [AsciiHash("")]
    Unknown = 0,

    /// <summary>
    /// Master role.
    /// </summary>
    [AsciiHash("master")]
    Master,

    /// <summary>
    /// Slave role (deprecated term).
    /// </summary>
    [AsciiHash("slave")]
    Slave,

    /// <summary>
    /// Replica role (preferred term for slave).
    /// </summary>
    [AsciiHash("replica")]
    Replica,

    /// <summary>
    /// Sentinel role.
    /// </summary>
    [AsciiHash("sentinel")]
    Sentinel,
}

/// <summary>
/// Metadata and parsing methods for RoleType.
/// </summary>
internal static partial class RoleTypeMetadata
{
    [AsciiHash]
    internal static partial bool TryParse(ReadOnlySpan<byte> value, out RoleType role);
}
