using System;
using RESPite;

namespace StackExchange.Redis;

/// <summary>
/// Fields that can appear in an XINFO GROUPS command response.
/// </summary>
[AsciiHash(nameof(StreamGroupInfoFieldMetadata))]
internal enum StreamGroupInfoField
{
    /// <summary>
    /// Unknown or unrecognized field.
    /// </summary>
    [AsciiHash("")]
    Unknown = 0,

    /// <summary>
    /// Group name.
    /// </summary>
    [AsciiHash("name")]
    Name,

    /// <summary>
    /// Number of consumers in the group.
    /// </summary>
    [AsciiHash("consumers")]
    Consumers,

    /// <summary>
    /// Number of pending messages.
    /// </summary>
    [AsciiHash("pending")]
    Pending,

    /// <summary>
    /// Last delivered ID.
    /// </summary>
    [AsciiHash("last-delivered-id")]
    LastDeliveredId,

    /// <summary>
    /// Number of entries read.
    /// </summary>
    [AsciiHash("entries-read")]
    EntriesRead,

    /// <summary>
    /// Lag value.
    /// </summary>
    [AsciiHash("lag")]
    Lag,
}

/// <summary>
/// Metadata and parsing methods for StreamGroupInfoField.
/// </summary>
internal static partial class StreamGroupInfoFieldMetadata
{
    [AsciiHash]
    internal static partial bool TryParse(ReadOnlySpan<byte> value, out StreamGroupInfoField field);
}
