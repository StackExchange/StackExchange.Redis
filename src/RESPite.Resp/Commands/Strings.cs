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
    public static RespCommand<SimpleString, long> STRLEN { get; }
        = new(PinnedPrefixWriter.SimpleString("*2\r\n$6\r\nSTRLEN\r\n"u8), RespReaders.Int64);

    /// <summary>
    /// Set key to hold the string value and set key to timeout after a given number of seconds.
    /// </summary>
    public static RespCommand<(SimpleString Key, int Seconds, ReadOnlySequence<byte> Value), Empty> SETEX { get; }
        = new(PinnedPrefixWriter.SimpleStringInt32Sequence("*4\r\n$5\r\nSETEX\r\n"u8), RespReaders.OK);

    /// <summary>
    /// Get the value of key. If the key does not exist the special value nil is returned. An error is returned if the value stored at key is not a string, because GET only handles string values.
    /// </summary>
    public static RespCommand<SimpleString, LeasedString> GET { get; }
        = new(PinnedPrefixWriter.SimpleString("*2\r\n$3\r\nGET\r\n"u8), RespReaders.LeasedString);

    /// <inheritdoc cref="GETRANGE"/>
    [Obsolete("Prefer " + nameof(GETRANGE))]
    public static RespCommand<(SimpleString Key, int Start, int End), LeasedString> SUBSTR { get; }
        = new(PinnedPrefixWriter.SimpleStringInt32Int32("*4\r\n$6\r\nSUBSTR\r\n"u8), RespReaders.LeasedString);

    /// <summary>
    /// Returns the substring of the string value stored at key, determined by the offsets start and end (both are inclusive). Negative offsets can be used in order to provide an offset starting from the end of the string. So -1 means the last character, -2 the penultimate and so forth.
    /// </summary>
    public static RespCommand<(SimpleString Key, int Start, int End), LeasedString> GETRANGE { get; }
        = new(PinnedPrefixWriter.SimpleStringInt32Int32("*4\r\n$8\r\nGETRANGE\r\n"u8), RespReaders.LeasedString);
}
