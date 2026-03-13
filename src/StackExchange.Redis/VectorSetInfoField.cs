using System;
using RESPite;

namespace StackExchange.Redis;

/// <summary>
/// Represents fields in a VSET.INFO response.
/// </summary>
internal enum VectorSetInfoField
{
    /// <summary>
    /// Unknown or unrecognized field.
    /// </summary>
    [AsciiHash("")]
    Unknown = 0,

    /// <summary>
    /// The size field.
    /// </summary>
    [AsciiHash("size")]
    Size,

    /// <summary>
    /// The vset-uid field.
    /// </summary>
    [AsciiHash("vset-uid")]
    VsetUid,

    /// <summary>
    /// The max-level field.
    /// </summary>
    [AsciiHash("max-level")]
    MaxLevel,

    /// <summary>
    /// The vector-dim field.
    /// </summary>
    [AsciiHash("vector-dim")]
    VectorDim,

    /// <summary>
    /// The quant-type field.
    /// </summary>
    [AsciiHash("quant-type")]
    QuantType,

    /// <summary>
    /// The hnsw-max-node-uid field.
    /// </summary>
    [AsciiHash("hnsw-max-node-uid")]
    HnswMaxNodeUid,
}

/// <summary>
/// Metadata and parsing methods for VectorSetInfoField.
/// </summary>
internal static partial class VectorSetInfoFieldMetadata
{
    [AsciiHash]
    internal static partial bool TryParse(ReadOnlySpan<byte> value, out VectorSetInfoField field);
}
