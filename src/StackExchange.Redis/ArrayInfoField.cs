using System;
using RESPite;

namespace StackExchange.Redis;

/// <summary>
/// Represents fields in an ARINFO response.
/// </summary>
internal enum ArrayInfoField
{
    /// <summary>
    /// Unknown or unrecognized field.
    /// </summary>
    [AsciiHash("")]
    Unknown = 0,

    /// <summary>
    /// The count field.
    /// </summary>
    [AsciiHash("count")]
    Count,

    /// <summary>
    /// The len field.
    /// </summary>
    [AsciiHash("len")]
    Length,

    /// <summary>
    /// The next-insert-index field.
    /// </summary>
    [AsciiHash("next-insert-index")]
    NextInsertIndex,

    /// <summary>
    /// The slices field.
    /// </summary>
    [AsciiHash("slices")]
    Slices,

    /// <summary>
    /// The directory-size field.
    /// </summary>
    [AsciiHash("directory-size")]
    DirectorySize,

    /// <summary>
    /// The super-dir-entries field.
    /// </summary>
    [AsciiHash("super-dir-entries")]
    SuperDirEntries,

    /// <summary>
    /// The slice-size field.
    /// </summary>
    [AsciiHash("slice-size")]
    SliceSize,
}

/// <summary>
/// Metadata and parsing methods for ArrayInfoField.
/// </summary>
internal static partial class ArrayInfoFieldMetadata
{
    [AsciiHash]
    internal static partial bool TryParse(ReadOnlySpan<byte> value, out ArrayInfoField field);
}
