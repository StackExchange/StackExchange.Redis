using System;

namespace RESPite.Resp.Commands;

/// <summary>
/// Operations relating to binary string payloads.
/// </summary>
public static class Strings
{
    /// <summary>
    /// Returns the length of the string value stored at key. An error is returned when key holds a non-string value.
    /// </summary>
    public static RespCommand<SimpleString, long> STRLEN { get; } = new(default);

    /// <summary>
    /// Set key to hold the string value and set key to timeout after a given number of seconds.
    /// </summary>
    public static RespCommand<(SimpleString Key, int Seconds, SimpleString Value), Empty> SETEX { get; } = new(default);

    /// <summary>
    /// Set key to hold the string value and set key to timeout after a given number of seconds.
    /// </summary>
    public static RespCommand<(SimpleString Key, SimpleString Value), Empty> SET { get; } = new(default);

    /// <summary>
    /// Get the value of key. If the key does not exist the special value nil is returned. An error is returned if the value stored at key is not a string, because GET only handles string values.
    /// </summary>
    public static RespCommand<SimpleString, LeasedString> GET { get; } = new(default);

    /// <inheritdoc cref="GETRANGE"/>
    [Obsolete("Prefer " + nameof(GETRANGE))]
    public static RespCommand<(SimpleString Key, int Start, int End), LeasedString> SUBSTR { get; } = new(default);

    /// <summary>
    /// Returns the substring of the string value stored at key, determined by the offsets start and end (both are inclusive). Negative offsets can be used in order to provide an offset starting from the end of the string. So -1 means the last character, -2 the penultimate and so forth.
    /// </summary>
    public static RespCommand<(SimpleString Key, int Start, int End), LeasedString> GETRANGE { get; } = new(default);
}
