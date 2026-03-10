using System;
using System.Diagnostics.CodeAnalysis;
using RESPite;

namespace StackExchange.Redis;

/// <summary>
/// Specifies the quantization type for vectors in a vectorset.
/// </summary>
[Experimental(Experiments.VectorSets, UrlFormat = Experiments.UrlFormat)]
public enum VectorSetQuantization
{
    /// <summary>
    /// Unknown or unrecognized quantization type.
    /// </summary>
    [AsciiHash("")]
    Unknown = 0,

    /// <summary>
    /// No quantization (full precision). This maps to "NOQUANT" or "f32".
    /// </summary>
    [AsciiHash("f32")]
    None = 1,

    /// <summary>
    /// 8-bit integer quantization (default). This maps to "Q8" or "int8".
    /// </summary>
    [AsciiHash("int8")]
    Int8 = 2,

    /// <summary>
    /// Binary quantization. This maps to "BIN" or "bin".
    /// </summary>
    [AsciiHash("bin")]
    Binary = 3,
}

/// <summary>
/// Metadata and parsing methods for VectorSetQuantization.
/// </summary>
internal static partial class VectorSetQuantizationMetadata
{
    [AsciiHash]
    internal static partial bool TryParse(ReadOnlySpan<byte> value, out VectorSetQuantization quantization);
}
