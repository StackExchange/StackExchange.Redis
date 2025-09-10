using System.Diagnostics.CodeAnalysis;

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
    Unknown = 0,

    /// <summary>
    /// No quantization (full precision). This maps to "NOQUANT" or "f32".
    /// </summary>
    None = 1,

    /// <summary>
    /// 8-bit integer quantization (default). This maps to "Q8" or "int8".
    /// </summary>
    Int8 = 2,

    /// <summary>
    /// Binary quantization. This maps to "BIN" or "bin".
    /// </summary>
    Binary = 3,
}
