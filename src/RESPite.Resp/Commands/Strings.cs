using System;
using RESPite.Resp.Readers;
using RESPite.Resp.Writers;

namespace RESPite.Resp.Commands;

/// <summary>
/// Operations relating to binary string payloads.
/// </summary>
public static class Strings
{
    /// <summary>
    /// Returns the length of the string value stored at key. An error is returned when key holds a non-string value.
    /// </summary>
    public static RespCommand<ReadOnlyMemory<byte>, long> STRLEN { get; } = new(PinnedPrefixWriter.Memory("*2\r\n$6\r\nSTRLEN\r\n"u8), RespReaders.Int64);
}
