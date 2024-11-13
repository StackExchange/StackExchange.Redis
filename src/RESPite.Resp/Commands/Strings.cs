using System;
using System.Buffers;
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
    public static RespCommand<SimpleString, long> STRLEN { get; } = new(PinnedPrefixWriter.SimpleString("*2\r\n$6\r\nSTRLEN\r\n"u8), RespReaders.Int64);

    /// <summary>
    /// Set key to hold the string value and set key to timeout after a given number of seconds.
    /// </summary>
    public static RespCommand<(SimpleString Key, int Seconds, ReadOnlySequence<byte> Value), Empty> SETEX { get; }
        = new(PinnedPrefixWriter.SimpleStringInt32Sequence("*4\r\n$5\r\nSETEX\r\n"u8), RespReaders.OK);
}
