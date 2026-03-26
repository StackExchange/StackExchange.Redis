using System;
using RESPite;

namespace StackExchange.Redis;

/// <summary>
/// Fields that can appear in an LCS (Longest Common Subsequence) command response.
/// </summary>
internal enum LCSField
{
    /// <summary>
    /// Unknown or unrecognized field.
    /// </summary>
    [AsciiHash("")]
    Unknown = 0,

    /// <summary>
    /// The matches array.
    /// </summary>
    [AsciiHash("matches")]
    Matches,

    /// <summary>
    /// The length value.
    /// </summary>
    [AsciiHash("len")]
    Len,
}

/// <summary>
/// Metadata and parsing methods for LCSField.
/// </summary>
internal static partial class LCSFieldMetadata
{
    [AsciiHash]
    internal static partial bool TryParse(ReadOnlySpan<byte> value, out LCSField field);
}
