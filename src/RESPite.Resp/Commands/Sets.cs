using System;
using RESPite.Resp.Readers;
using RESPite.Resp.Writers;

namespace RESPite.Resp.Commands;

/// <summary>
/// Operations relating to binary string payloads.
/// </summary>
public static class Sets
{
    /// <summary>
    /// Returns the set cardinality (number of elements) of the set stored at key.
    /// </summary>
    public static RespCommand<ReadOnlyMemory<byte>, long> SCARD { get; } = new(PinnedPrefixWriter.Memory("*2\r\n$5\r\nSCARD\r\n"u8), RespReaders.Int64);
}
