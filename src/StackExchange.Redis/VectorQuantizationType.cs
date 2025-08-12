using System.Diagnostics.CodeAnalysis;

namespace StackExchange.Redis;

/// <summary>
/// Specifies the quantization type for vectors in a vectorset.
/// </summary>
[Experimental(Experiments.VectorSets, UrlFormat = Experiments.UrlFormat)]
public enum VectorQuantizationType
{
    /// <summary>
    /// Unknown or unrecognized quantization type.
    /// </summary>
    Unknown,

    /// <summary>
    /// No quantization (full precision).
    /// </summary>
    None,

    /// <summary>
    /// 8-bit integer quantization (default).
    /// </summary>
    Int8,

    /// <summary>
    /// Binary quantization.
    /// </summary>
    Binary,
}
