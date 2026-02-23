using System;
using RESPite;

namespace StackExchange.Redis;

/// <summary>
/// Fields that can appear in a HELLO command response.
/// </summary>
[AsciiHash(nameof(HelloFieldMetadata))]
internal enum HelloField
{
    /// <summary>
    /// Unknown or unrecognized field.
    /// </summary>
    [AsciiHash("")]
    Unknown = 0,

    /// <summary>
    /// Redis server version.
    /// </summary>
    [AsciiHash("version")]
    Version,

    /// <summary>
    /// Protocol version (2 or 3).
    /// </summary>
    [AsciiHash("proto")]
    Proto,

    /// <summary>
    /// Connection ID.
    /// </summary>
    [AsciiHash("id")]
    Id,

    /// <summary>
    /// Server mode (standalone, cluster, sentinel).
    /// </summary>
    [AsciiHash("mode")]
    Mode,

    /// <summary>
    /// Server role (master/primary, slave/replica).
    /// </summary>
    [AsciiHash("role")]
    Role,
}

/// <summary>
/// Metadata and parsing methods for HelloField.
/// </summary>
internal static partial class HelloFieldMetadata
{
    [AsciiHash]
    internal static partial bool TryParse(ReadOnlySpan<byte> value, out HelloField field);
}
