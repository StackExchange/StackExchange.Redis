using System;
using RESPite.Resp.Readers;
using RESPite.Resp.Writers;

namespace RESPite.Resp.Commands;

/// <summary>
/// Operations relating to sorted sets.
/// </summary>
public static class Hashes
{
    /// <summary>
    /// Returns the sorted set cardinality (number of elements) of the sorted set stored at key.
    /// </summary>
    public static RespCommand<SimpleString, long> HLEN { get; } = new(PinnedPrefixWriter.SimpleString("*2\r\n$4\r\nHLEN\r\n"u8), RespReaders.Int64);
}
