using System;
using RESPite;

namespace StackExchange.Redis;

internal enum PushKind
{
    [AsciiHash("")]
    None,
    [AsciiHash("message")]
    Message,
    [AsciiHash("pmessage")]
    PMessage,
    [AsciiHash("smessage")]
    SMessage,
    [AsciiHash("subscribe")]
    Subscribe,
    [AsciiHash("psubscribe")]
    PSubscribe,
    [AsciiHash("ssubscribe")]
    SSubscribe,
    [AsciiHash("unsubscribe")]
    Unsubscribe,
    [AsciiHash("punsubscribe")]
    PUnsubscribe,
    [AsciiHash("sunsubscribe")]
    SUnsubscribe,

    // the maintenance-notification family; these are *not* pub/sub - element 1 is a sequence number
    // rather than a channel, so they must be dispatched before anything reads a channel name. The specs
    // write them uppercase while the pub/sub kinds above are lowercase, hence the case-insensitive match
    //
    // NOTE: these must stay contiguous, and nothing may be inserted between them: the dispatch below
    // tests the family with a range check (`>= Moving and <= SlotMigrated`) rather than listing them.
    [AsciiHash("MOVING")]
    Moving,
    [AsciiHash("MIGRATING")]
    Migrating,
    [AsciiHash("MIGRATED")]
    Migrated,
    [AsciiHash("FAILING_OVER")]
    FailingOver,
    [AsciiHash("FAILED_OVER")]
    FailedOver,
    [AsciiHash("SMIGRATING")]
    SlotMigrating,
    [AsciiHash("SMIGRATED")]
    SlotMigrated,

    /// <summary>
    /// Server-assisted client-side caching: a key we read has changed, or (with a null payload)
    /// everything has. Unlike every other kind here, the second element is not a channel.
    /// </summary>
    /// <remarks>
    /// Deliberately after the maintenance family rather than among it, so the range check that
    /// dispatches that family cannot pick this up: it is out-of-band for the same reason, but it is
    /// not one of them and is handled separately.
    /// </remarks>
    [AsciiHash("invalidate")]
    Invalidate,
}

internal static partial class PushKindMetadata
{
    /// <summary>
    /// Identifies a push frame from its first element.
    /// </summary>
    /// <remarks>
    /// Case-insensitive: the pub/sub kinds are lowercase on the wire and the maintenance kinds are
    /// uppercase, and no specification anywhere is careful about it - so don't bake in an assumption.
    /// </remarks>
    [AsciiHash(CaseSensitive = false)]
    internal static partial bool TryParse(ReadOnlySpan<byte> value, out PushKind result);
}
