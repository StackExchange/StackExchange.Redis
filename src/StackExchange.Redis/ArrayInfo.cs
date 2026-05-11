namespace StackExchange.Redis;

/// <summary>
/// Contains metadata information about an array returned by the ARINFO command.
/// </summary>
public readonly struct ArrayInfo(
    long count,
    long length,
    long nextInsertIndex,
    long slices,
    long directorySize,
    long superDirEntries,
    long sliceSize)
{
    /// <summary>
    /// The number of array cells that have values.
    /// </summary>
    public long Count { get; } = count;

    /// <summary>
    /// The notional length of the array.
    /// </summary>
    public long Length { get; } = length;

    /// <summary>
    /// The current array write-head.
    /// </summary>
    public long NextInsertIndex { get; } = nextInsertIndex;

    /// <summary>
    /// The number of slices used by the array.
    /// </summary>
    public long Slices { get; } = slices;

    /// <summary>
    /// The size of the array directory.
    /// </summary>
    public long DirectorySize { get; } = directorySize;

    /// <summary>
    /// The number of super-directory entries.
    /// </summary>
    public long SuperDirEntries { get; } = superDirEntries;

    /// <summary>
    /// The configured slice size.
    /// </summary>
    public long SliceSize { get; } = sliceSize;
}
