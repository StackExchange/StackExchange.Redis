using System;
using RESPite.Resp.Commands;
using static RESPite.Resp.Client.CommandFactory;

namespace RESPite.Resp.KeyValueStore;

/// <summary>
/// Operations relating to binary string payloads.
/// </summary>
public static class Strings
{
    /// <summary>
    /// Returns the length of the string value stored at key. An error is returned when key holds a non-string value.
    /// </summary>
    public static readonly RespCommand<SimpleString, long> STRLEN = new(Default);

    /// <summary>
    /// Set key to hold the string value and set key to timeout after a given number of seconds.
    /// </summary>
    public static readonly RespCommand<(SimpleString Key, int Seconds, SimpleString Value), Empty> SETEX = new(Default);

    /// <summary>
    /// Set key to hold the string value and set key to timeout after a given number of seconds.
    /// </summary>
    public static readonly RespCommand<(SimpleString Key, SimpleString Value), Empty> SET = new(Default);

    /// <summary>
    /// Get the value of key. If the key does not exist the special value nil is returned. An error is returned if the value stored at key is not a string, because GET only handles string values.
    /// </summary>
    public static readonly RespCommand<SimpleString, LeasedString> GET = new(Default);

    /// <inheritdoc cref="GETRANGE"/>
    [Obsolete("Prefer " + nameof(GETRANGE))]
    public static readonly RespCommand<(SimpleString Key, int Start, int End), LeasedString> SUBSTR = new(Default);

    /// <summary>
    /// Returns the substring of the string value stored at key, determined by the offsets start and end (both are inclusive). Negative offsets can be used in order to provide an offset starting from the end of the string. So -1 means the last character, -2 the penultimate and so forth.
    /// </summary>
    public static readonly RespCommand<(SimpleString Key, int Start, int End), LeasedString> GETRANGE = new(Default);
}
