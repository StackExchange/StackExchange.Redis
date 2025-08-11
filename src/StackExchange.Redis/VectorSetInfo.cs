using System;

namespace StackExchange.Redis
{
    /// <summary>
    /// Contains metadata information about a vectorset returned by VINFO command.
    /// </summary>
    public readonly struct VectorSetInfo(VectorQuantizationType quantizationType, int dimension, long length, int maxLevel, long vectorSetUid, long hnswMaxNodeUid)
    {
        /// <summary>
        /// The quantization type used for vectors in this vectorset.
        /// </summary>
        public VectorQuantizationType QuantizationType { get; } = quantizationType;

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
}
