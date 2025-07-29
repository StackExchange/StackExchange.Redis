namespace StackExchange.Redis;

/// <summary>
/// Determines how stream trimming works.
/// </summary>
public enum StreamTrimResult
{
    /// <summary>
    /// No such id exists in the provided stream key.
    /// </summary>
    NotFound = -1,

    /// <summary>
    /// Entry was deleted from the stream.
    /// </summary>
    Deleted = 1,

    /// <summary>
    /// Entry was not deleted because it has either not been delivered to any consumer, or
    /// still has references in the consumer groups' Pending Entries List (PEL).
    /// </summary>
    /// <remarks>This response relates to the <see cref="StreamTrimMode.Acknowledged"/> mode.</remarks>
    NotDeleted = 2,
}
