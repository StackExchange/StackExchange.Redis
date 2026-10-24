using System;
using RESPite;

namespace StackExchange.Redis;

/// <summary>
/// Fields that can appear in a HELLO command response.
/// </summary>
internal enum HelloField
{
    /// <summary>
    /// Unknown or unrecognized field.
    /// </summary>
    [AsciiHash("")]
    Unknown = 0,

    /// <summary>
    /// The server product: <c>redis</c> for Redis and for servers that present as Redis (Garnet); Valkey reports <c>valkey</c>.
    /// </summary>
    [AsciiHash("server")]
    Server,

    /// <summary>
    /// Server version. This is only the Redis API version when <see cref="Server"/> is <c>redis</c>; Valkey reports its own
    /// version here (<c>9.1.2</c>, say, while <c>INFO</c> still gives <c>redis_version:7.2.4</c>).
    /// </summary>
    [AsciiHash("version")]
    Version,

    /// <summary>
    /// Garnet's own version; Garnet reports <c>server</c> as <c>redis</c> and <see cref="Version"/> as the Redis API version.
    /// </summary>
    [AsciiHash("garnet_version")]
    GarnetVersion,

    /// <summary>
    /// Valkey's own version, if reported alongside <see cref="Server"/>.
    /// </summary>
    [AsciiHash("valkey_version")]
    ValkeyVersion,

    /// <summary>
    /// Dragonfly's own version, if reported.
    /// </summary>
    [AsciiHash("dragonfly_version")]
    DragonflyVersion,

    /// <summary>
    /// Memurai's own version, if reported.
    /// </summary>
    [AsciiHash("memurai_version")]
    MemuraiVersion,

    /// <summary>
    /// Redict's own version, if reported.
    /// </summary>
    [AsciiHash("redict_version")]
    RedictVersion,

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
