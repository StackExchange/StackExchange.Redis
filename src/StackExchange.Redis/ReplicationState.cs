using System;
using RESPite;

namespace StackExchange.Redis;

/// <summary>
/// Redis replication states.
/// </summary>
[AsciiHash(nameof(ReplicationStateMetadata))]
internal enum ReplicationState
{
    /// <summary>
    /// Unknown or unrecognized state.
    /// </summary>
    [AsciiHash("")]
    Unknown = 0,

    /// <summary>
    /// Connect state.
    /// </summary>
    [AsciiHash("connect")]
    Connect,

    /// <summary>
    /// Connecting state.
    /// </summary>
    [AsciiHash("connecting")]
    Connecting,

    /// <summary>
    /// Sync state.
    /// </summary>
    [AsciiHash("sync")]
    Sync,

    /// <summary>
    /// Connected state.
    /// </summary>
    [AsciiHash("connected")]
    Connected,

    /// <summary>
    /// None state.
    /// </summary>
    [AsciiHash("none")]
    None,

    /// <summary>
    /// Handshake state.
    /// </summary>
    [AsciiHash("handshake")]
    Handshake,
}

/// <summary>
/// Metadata and parsing methods for ReplicationState.
/// </summary>
internal static partial class ReplicationStateMetadata
{
    [AsciiHash]
    internal static partial bool TryParse(ReadOnlySpan<byte> value, out ReplicationState state);
}
