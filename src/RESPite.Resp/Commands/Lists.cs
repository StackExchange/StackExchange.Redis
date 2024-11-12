using System;
using RESPite.Resp.Readers;
using RESPite.Resp.Writers;

namespace RESPite.Resp.Commands;

/// <summary>
/// Operations relating to lists.
/// </summary>
public static class Lists
{
    /// <summary>
    /// Returns the length of the list stored at key.
    /// </summary>
    public static RespCommand<ReadOnlyMemory<byte>, long> LLEN { get; } = new(PinnedPrefixWriter.Memory("*2\r\n$4\r\nLLEN\r\n"u8), RespReaders.Int64);
}
