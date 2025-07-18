namespace StackExchange.Redis;

/// <summary>
/// Determines how stream trimming works.
/// </summary>
public enum StreamDeleteResult
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
    /// Entry was not deleted, but there are still dangling references.
    /// </summary>
    /// <remarks>This response relates to the <see cref="StreamDeleteMode.Acknowledged"/> mode.</remarks>
    NotDeleted = 2,
}
