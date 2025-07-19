namespace StackExchange.Redis;

/// <summary>
/// Determines how stream trimming works.
/// </summary>
public enum StreamTrimMode
{
    /// <summary>
    /// Trims the stream according to the specified policy (MAXLEN or MINID) regardless of whether entries are referenced by any consumer groups, but preserves existing references to these entries in all consumer groups' PEL.
    /// </summary>
    KeepReferences = 0,

    /// <summary>
    /// Trims the stream according to the specified policy and also removes all references to the trimmed entries from all consumer groups' PEL.
    /// </summary>
    /// <remarks>Requires server 8.2 or above.</remarks>
    DeleteReferences = 1,

    /// <summary>
    /// With ACKED: Only trims entries that were read and acknowledged by all consumer groups.
    /// </summary>
    /// <remarks>Requires server 8.2 or above.</remarks>
    Acknowledged = 2,
}
