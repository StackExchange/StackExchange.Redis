using System;
using RESPite;

namespace StackExchange.Redis;

/// <summary>
/// Fields that can appear in INFO output during auto-configuration.
/// </summary>
internal enum AutoConfigureInfoField
{
    /// <summary>
    /// Unknown or unrecognized field.
    /// </summary>
    [AsciiHash("")]
    Unknown = 0,

    [AsciiHash("role")]
    Role,

    [AsciiHash("master_host")]
    MasterHost,

    [AsciiHash("master_port")]
    MasterPort,

    [AsciiHash("redis_version")]
    RedisVersion,

    [AsciiHash("redis_mode")]
    RedisMode,

    [AsciiHash("run_id")]
    RunId,

    [AsciiHash("garnet_version")]
    GarnetVersion,

    [AsciiHash("valkey_version")]
    ValkeyVersion,

    [AsciiHash("server_mode")]
    ServerMode,
}

/// <summary>
/// Metadata and parsing methods for <see cref="AutoConfigureInfoField"/>.
/// </summary>
internal static partial class AutoConfigureInfoFieldMetadata
{
    [AsciiHash]
    internal static partial bool TryParse(ReadOnlySpan<char> value, out AutoConfigureInfoField field);
}
