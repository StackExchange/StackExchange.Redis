using System.Diagnostics.CodeAnalysis;
using RESPite;

namespace StackExchange.Redis;

/// <summary>
/// Contains metadata information about an array returned by the ARINFO command.
/// </summary>
[Experimental(Experiments.Server_8_8, UrlFormat = Experiments.UrlFormat)]
public readonly struct ArrayInfo(
    RedisArrayIndex count,
    RedisArrayIndex length,
    RedisArrayIndex nextInsertIndex,
    RedisArrayIndex slices,
    RedisArrayIndex directorySize,
    RedisArrayIndex superDirEntries,
    RedisArrayIndex sliceSize)
{
    /// <summary>
    /// The number of array cells that have values.
    /// </summary>
    public RedisArrayIndex Count { get; } = count;

    /// <summary>
    /// The notional length of the array.
    /// </summary>
    public RedisArrayIndex Length { get; } = length;

    /// <summary>
    /// The current array write-head.
    /// </summary>
    public RedisArrayIndex NextInsertIndex { get; } = nextInsertIndex;

    /// <summary>
    /// The number of slices used by the array.
    /// </summary>
    public RedisArrayIndex Slices { get; } = slices;

    /// <summary>
    /// The size of the array directory.
    /// </summary>
    public RedisArrayIndex DirectorySize { get; } = directorySize;

    /// <summary>
    /// The number of super-directory entries.
    /// </summary>
    public RedisArrayIndex SuperDirEntries { get; } = superDirEntries;

    /// <summary>
    /// The configured slice size.
    /// </summary>
    public RedisArrayIndex SliceSize { get; } = sliceSize;
}
