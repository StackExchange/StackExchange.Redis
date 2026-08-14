using System;
using RESPite;

namespace StackExchange.Redis;

/// <summary>
/// Keys that can appear in the per-node metadata map of a <c>CLUSTER SLOTS</c> reply.
/// </summary>
/// <remarks>
/// Matched case-insensitively on purpose: the documentation renders these as <c>IP</c>/<c>Hostname</c> in
/// prose but lower-case in its examples, so the casing cannot be relied upon. The set is documented as
/// extensible, hence <see cref="Unknown"/> - an unrecognized key is preserved rather than discarded.
/// </remarks>
internal enum ClusterSlotMetadataKey
{
    /// <summary>A key this library does not recognize.</summary>
    [AsciiHash("")]
    Unknown = 0,

    /// <summary>The node's address, supplied when the reported endpoint is some other form.</summary>
    [AsciiHash("ip")]
    Ip,

    /// <summary>The node's announced hostname, supplied when the reported endpoint is some other form.</summary>
    [AsciiHash("hostname")]
    Hostname,
}

/// <summary>
/// Metadata and parsing methods for <see cref="ClusterSlotMetadataKey"/>.
/// </summary>
internal static partial class ClusterSlotMetadataKeyMetadata
{
    [AsciiHash(CaseSensitive = false)]
    internal static partial bool TryParse(ReadOnlySpan<byte> value, out ClusterSlotMetadataKey key);
}
