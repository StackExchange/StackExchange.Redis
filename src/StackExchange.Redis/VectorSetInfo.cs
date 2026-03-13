using System;
using System.Diagnostics.CodeAnalysis;
using RESPite;

namespace StackExchange.Redis;

/// <summary>
/// Contains metadata information about a vectorset returned by VINFO command.
/// </summary>
[Experimental(Experiments.VectorSets, UrlFormat = Experiments.UrlFormat)]
public readonly struct VectorSetInfo(
    VectorSetQuantization quantization,
    string? quantizationRaw,
    int dimension,
    long length,
    int maxLevel,
    long vectorSetUid,
    long hnswMaxNodeUid)
{
    /// <summary>
    /// The quantization type used for vectors in this vectorset.
    /// </summary>
    public VectorSetQuantization Quantization { get; } = quantization;

    /// <summary>
    /// The raw representation of the quantization type used for vectors in this vectorset. This is only
    /// populated if the <see cref="Quantization"/> is <see cref="VectorSetQuantization.Unknown"/>.
    /// </summary>
    public string? QuantizationRaw { get; } = quantizationRaw;

    /// <summary>
    /// The number of dimensions in each vector.
    /// </summary>
    public int Dimension { get; } = dimension;

    /// <summary>
    /// The number of elements (cardinality) in the vectorset.
    /// </summary>
    public long Length { get; } = length;

    /// <summary>
    /// The maximum level in the HNSW graph structure.
    /// </summary>
    public int MaxLevel { get; } = maxLevel;

    /// <summary>
    /// The unique identifier for this vectorset.
    /// </summary>
    public long VectorSetUid { get; } = vectorSetUid;

    /// <summary>
    /// The maximum node unique identifier in the HNSW graph.
    /// </summary>
    public long HnswMaxNodeUid { get; } = hnswMaxNodeUid;
}
