using System.Diagnostics.CodeAnalysis;

namespace StackExchange.Redis;

/// <summary>
/// Represents a result from vector similarity search operations.
/// </summary>
[Experimental(Experiments.VectorSets, UrlFormat = Experiments.UrlFormat)]
public readonly struct VectorSetSimilaritySearchResult(RedisValue member, double score = double.NaN, string? attributesJson = null)
{
    /// <summary>
    /// The member name/identifier in the vectorset.
    /// </summary>
    public RedisValue Member { get; } = member;

    /// <summary>
    /// The similarity score (0-1) when WITHSCORES is used, NaN otherwise.
    /// A score of 1 means identical vectors, 0 means opposite vectors.
    /// </summary>
    public double Score { get; } = score;

    /// <summary>
    /// The JSON attributes associated with the member when WITHATTRIBS is used, null otherwise.
    /// </summary>
    public string? AttributesJson { get; } = attributesJson;
}
