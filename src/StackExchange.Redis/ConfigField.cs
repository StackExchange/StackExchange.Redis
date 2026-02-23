using System;
using RESPite;

namespace StackExchange.Redis;

/// <summary>
/// Fields that can appear in a CONFIG GET command response.
/// </summary>
internal enum ConfigField
{
    /// <summary>
    /// Unknown or unrecognized field.
    /// </summary>
    [AsciiHash("")]
    Unknown = 0,

    /// <summary>
    /// Timeout configuration.
    /// </summary>
    [AsciiHash("timeout")]
    Timeout,

    /// <summary>
    /// Number of databases.
    /// </summary>
    [AsciiHash("databases")]
    Databases,

    /// <summary>
    /// Replica read-only setting (slave-read-only).
    /// </summary>
    [AsciiHash("slave-read-only")]
    SlaveReadOnly,

    /// <summary>
    /// Replica read-only setting (replica-read-only).
    /// </summary>
    [AsciiHash("replica-read-only")]
    ReplicaReadOnly,
}

/// <summary>
/// Metadata and parsing methods for ConfigField.
/// </summary>
internal static partial class ConfigFieldMetadata
{
    [AsciiHash]
    internal static partial bool TryParse(ReadOnlySpan<byte> value, out ConfigField field);
}
