using System;
using RESPite;

namespace StackExchange.Redis;

/// <summary>
/// Yes/No values in Redis responses.
/// </summary>
internal enum YesNo
{
    /// <summary>
    /// Unknown or unrecognized value.
    /// </summary>
    [AsciiHash("")]
    Unknown = 0,

    /// <summary>
    /// Yes value.
    /// </summary>
    [AsciiHash("yes")]
    Yes,

    /// <summary>
    /// No value.
    /// </summary>
    [AsciiHash("no")]
    No,
}

/// <summary>
/// Metadata and parsing methods for YesNo.
/// </summary>
internal static partial class YesNoMetadata
{
    [AsciiHash]
    internal static partial bool TryParse(ReadOnlySpan<byte> value, out YesNo yesNo);
}
