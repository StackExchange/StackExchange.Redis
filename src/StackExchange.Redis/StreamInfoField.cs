using System;
using RESPite;

namespace StackExchange.Redis;

/// <summary>
/// Fields that can appear in a XINFO STREAM response.
/// </summary>
[AsciiHash(nameof(StreamInfoFieldMetadata))]
internal enum StreamInfoField
{
    /// <summary>
    /// Unknown or unrecognized field.
    /// </summary>
    [AsciiHash("")]
    Unknown = 0,

    /// <summary>
    /// The number of entries in the stream.
    /// </summary>
    [AsciiHash("length")]
    Length,

    /// <summary>
    /// The number of radix tree keys.
    /// </summary>
    [AsciiHash("radix-tree-keys")]
    RadixTreeKeys,

    /// <summary>
    /// The number of radix tree nodes.
    /// </summary>
    [AsciiHash("radix-tree-nodes")]
    RadixTreeNodes,

    /// <summary>
    /// The number of consumer groups.
    /// </summary>
    [AsciiHash("groups")]
    Groups,

    /// <summary>
    /// The last generated ID.
    /// </summary>
    [AsciiHash("last-generated-id")]
    LastGeneratedId,

    /// <summary>
    /// The first entry in the stream.
    /// </summary>
    [AsciiHash("first-entry")]
    FirstEntry,

    /// <summary>
    /// The last entry in the stream.
    /// </summary>
    [AsciiHash("last-entry")]
    LastEntry,

    /// <summary>
    /// The maximum deleted entry ID (Redis 7.0+).
    /// </summary>
    [AsciiHash("max-deleted-entry-id")]
    MaxDeletedEntryId,

    /// <summary>
    /// The recorded first entry ID (Redis 7.0+).
    /// </summary>
    [AsciiHash("recorded-first-entry-id")]
    RecordedFirstEntryId,

    /// <summary>
    /// The total number of entries added (Redis 7.0+).
    /// </summary>
    [AsciiHash("entries-added")]
    EntriesAdded,

    /// <summary>
    /// IDMP duration in seconds (Redis 8.6+).
    /// </summary>
    [AsciiHash("idmp-duration")]
    IdmpDuration,

    /// <summary>
    /// IDMP max size (Redis 8.6+).
    /// </summary>
    [AsciiHash("idmp-maxsize")]
    IdmpMaxsize,

    /// <summary>
    /// Number of PIDs tracked (Redis 8.6+).
    /// </summary>
    [AsciiHash("pids-tracked")]
    PidsTracked,

    /// <summary>
    /// Number of IIDs tracked (Redis 8.6+).
    /// </summary>
    [AsciiHash("iids-tracked")]
    IidsTracked,

    /// <summary>
    /// Number of IIDs added (Redis 8.6+).
    /// </summary>
    [AsciiHash("iids-added")]
    IidsAdded,

    /// <summary>
    /// Number of duplicate IIDs (Redis 8.6+).
    /// </summary>
    [AsciiHash("iids-duplicates")]
    IidsDuplicates,
}

/// <summary>
/// Metadata and parsing methods for StreamInfoField.
/// </summary>
internal static partial class StreamInfoFieldMetadata
{
    [AsciiHash]
    internal static partial bool TryParse(ReadOnlySpan<byte> value, out StreamInfoField field);
}
