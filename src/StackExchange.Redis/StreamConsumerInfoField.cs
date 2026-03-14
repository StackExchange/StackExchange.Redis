using System;
using RESPite;

namespace StackExchange.Redis;

/// <summary>
/// Fields that can appear in an XINFO CONSUMERS command response.
/// </summary>
internal enum StreamConsumerInfoField
{
    /// <summary>
    /// Unknown or unrecognized field.
    /// </summary>
    [AsciiHash("")]
    Unknown = 0,

    /// <summary>
    /// Consumer name.
    /// </summary>
    [AsciiHash("name")]
    Name,

    /// <summary>
    /// Number of pending messages.
    /// </summary>
    [AsciiHash("pending")]
    Pending,

    /// <summary>
    /// Idle time in milliseconds.
    /// </summary>
    [AsciiHash("idle")]
    Idle,
}

/// <summary>
/// Metadata and parsing methods for StreamConsumerInfoField.
/// </summary>
internal static partial class StreamConsumerInfoFieldMetadata
{
    [AsciiHash]
    internal static partial bool TryParse(ReadOnlySpan<byte> value, out StreamConsumerInfoField field);
}
