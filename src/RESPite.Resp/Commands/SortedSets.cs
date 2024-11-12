using System;
using RESPite.Resp.Readers;
using RESPite.Resp.Writers;

namespace RESPite.Resp.Commands;

/// <summary>
/// Operations relating to sorted sets.
/// </summary>
public static class SortedSets
{
    /// <summary>
    /// Returns the sorted set cardinality (number of elements) of the sorted set stored at key.
    /// </summary>
    public static RespCommand<ReadOnlyMemory<byte>, long> ZCARD { get; } = new(PinnedPrefixWriter.Memory("*2\r\n$5\r\nZCARD\r\n"u8), RespReaders.Int64);
}
