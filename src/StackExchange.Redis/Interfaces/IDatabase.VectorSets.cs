using System;
using System.Diagnostics.CodeAnalysis;

// ReSharper disable once CheckNamespace
namespace StackExchange.Redis;

/// <summary>
/// Describes functionality that is common to both standalone redis servers and redis clusters.
/// </summary>
public partial interface IDatabase
{
    // Vector Set operations

    /// <summary>
    /// Add a vector to a vectorset.
    /// </summary>
    /// <param name="key">The key of the vectorset.</param>
    /// <param name="element">The element name.</param>
    /// <param name="values">The vector data.</param>
    /// <param name="reducedDimensions">Optional dimension reduction using random projection (REDUCE parameter).</param>
    /// <param name="quantization">Quantization type - Int8 (Q8), None (NOQUANT), or Binary (BIN). Default: Int8.</param>
    /// <param name="buildExplorationFactor">Optional HNSW build exploration factor (EF parameter, default: 200).</param>
    /// <param name="maxConnections">Optional maximum connections per HNSW node (M parameter, default: 16).</param>
    /// <param name="useCheckAndSet">Optional check-and-set mode for partial threading (CAS parameter).</param>
    /// <param name="attributesJson">Optional JSON attributes for the element (SETATTR parameter).</param>
    /// <param name="flags">The flags to use for this operation.</param>
    /// <returns><see langword="true"/> if the element was added; <see langword="false"/> if it already existed.</returns>
    /// <remarks><seealso href="https://redis.io/commands/vadd"/></remarks>
    [Experimental(Experiments.VectorSets, UrlFormat = Experiments.UrlFormat)]
    bool VectorSetAdd(
        RedisKey key,
        RedisValue element,
        ReadOnlyMemory<float> values,
        int? reducedDimensions = null,
        VectorSetQuantization quantization = VectorSetQuantization.Int8,
        int? buildExplorationFactor = null,
        int? maxConnections = null,
        bool useCheckAndSet = false,
        string? attributesJson = null,
        CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Get the cardinality (number of elements) of a vectorset.
    /// </summary>
    /// <param name="key">The key of the vectorset.</param>
    /// <param name="flags">The flags to use for this operation.</param>
    /// <returns>The cardinality of the vectorset.</returns>
    /// <remarks><seealso href="https://redis.io/commands/vcard"/></remarks>
    [Experimental(Experiments.VectorSets, UrlFormat = Experiments.UrlFormat)]
    long VectorSetLength(RedisKey key, CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Get the dimension of vectors in a vectorset.
    /// </summary>
    /// <param name="key">The key of the vectorset.</param>
    /// <param name="flags">The flags to use for this operation.</param>
    /// <returns>The dimension of vectors in the vectorset.</returns>
    /// <remarks><seealso href="https://redis.io/commands/vdim"/></remarks>
    [Experimental(Experiments.VectorSets, UrlFormat = Experiments.UrlFormat)]
    int VectorSetDimension(RedisKey key, CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Get the vector for a member.
    /// </summary>
    /// <param name="key">The key of the vectorset.</param>
    /// <param name="member">The member name.</param>
    /// <param name="flags">The flags to use for this operation.</param>
    /// <returns>The vector as a pooled memory lease.</returns>
    /// <remarks><seealso href="https://redis.io/commands/vemb"/></remarks>
    [Experimental(Experiments.VectorSets, UrlFormat = Experiments.UrlFormat)]
    Lease<float>? VectorSetGetApproximateVector(
        RedisKey key,
        RedisValue member,
        CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Get JSON attributes for a member in a vectorset.
    /// </summary>
    /// <param name="key">The key of the vectorset.</param>
    /// <param name="member">The member name.</param>
    /// <param name="flags">The flags to use for this operation.</param>
    /// <returns>The attributes as a JSON string.</returns>
    /// <remarks><seealso href="https://redis.io/commands/vgetattr"/></remarks>
    [Experimental(Experiments.VectorSets, UrlFormat = Experiments.UrlFormat)]
    string? VectorSetGetAttributesJson(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Get information about a vectorset.
    /// </summary>
    /// <param name="key">The key of the vectorset.</param>
    /// <param name="flags">The flags to use for this operation.</param>
    /// <returns>Information about the vectorset.</returns>
    /// <remarks><seealso href="https://redis.io/commands/vinfo"/></remarks>
    [Experimental(Experiments.VectorSets, UrlFormat = Experiments.UrlFormat)]
    VectorSetInfo? VectorSetInfo(RedisKey key, CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Check if a member exists in a vectorset.
    /// </summary>
    /// <param name="key">The key of the vectorset.</param>
    /// <param name="member">The member name.</param>
    /// <param name="flags">The flags to use for this operation.</param>
    /// <returns>True if the member exists, false otherwise.</returns>
    /// <remarks><seealso href="https://redis.io/commands/vismember"/></remarks>
    [Experimental(Experiments.VectorSets, UrlFormat = Experiments.UrlFormat)]
    bool VectorSetContains(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Get links/connections for a member in a vectorset.
    /// </summary>
    /// <param name="key">The key of the vectorset.</param>
    /// <param name="member">The member name.</param>
    /// <param name="flags">The flags to use for this operation.</param>
    /// <returns>The linked members.</returns>
    /// <remarks><seealso href="https://redis.io/commands/vlinks"/></remarks>
    [Experimental(Experiments.VectorSets, UrlFormat = Experiments.UrlFormat)]
    Lease<RedisValue>? VectorSetGetLinks(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Get links/connections with scores for a member in a vectorset.
    /// </summary>
    /// <param name="key">The key of the vectorset.</param>
    /// <param name="member">The member name.</param>
    /// <param name="flags">The flags to use for this operation.</param>
    /// <returns>The linked members with their similarity scores.</returns>
    /// <remarks><seealso href="https://redis.io/commands/vlinks"/></remarks>
    [Experimental(Experiments.VectorSets, UrlFormat = Experiments.UrlFormat)]
    Lease<VectorSetLink>? VectorSetGetLinksWithScores(
        RedisKey key,
        RedisValue member,
        CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Get a random member from a vectorset.
    /// </summary>
    /// <param name="key">The key of the vectorset.</param>
    /// <param name="flags">The flags to use for this operation.</param>
    /// <returns>A random member from the vectorset, or null if the vectorset is empty.</returns>
    /// <remarks><seealso href="https://redis.io/commands/vrandmember"/></remarks>
    [Experimental(Experiments.VectorSets, UrlFormat = Experiments.UrlFormat)]
    RedisValue VectorSetRandomMember(RedisKey key, CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Get random members from a vectorset.
    /// </summary>
    /// <param name="key">The key of the vectorset.</param>
    /// <param name="count">The number of random members to return.</param>
    /// <param name="flags">The flags to use for this operation.</param>
    /// <returns>Random members from the vectorset.</returns>
    /// <remarks><seealso href="https://redis.io/commands/vrandmember"/></remarks>
    [Experimental(Experiments.VectorSets, UrlFormat = Experiments.UrlFormat)]
    RedisValue[] VectorSetRandomMembers(RedisKey key, long count, CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Remove a member from a vectorset.
    /// </summary>
    /// <param name="key">The key of the vectorset.</param>
    /// <param name="member">The member to remove.</param>
    /// <param name="flags">The flags to use for this operation.</param>
    /// <returns><see langword="true"/> if the member was removed; <see langword="false"/> if it was not found.</returns>
    /// <remarks><seealso href="https://redis.io/commands/vrem"/></remarks>
    [Experimental(Experiments.VectorSets, UrlFormat = Experiments.UrlFormat)]
    bool VectorSetRemove(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Set JSON attributes for a member in a vectorset.
    /// </summary>
    /// <param name="key">The key of the vectorset.</param>
    /// <param name="member">The member name.</param>
    /// <param name="jsonAttributes">The attributes to set as a JSON string.</param>
    /// <param name="flags">The flags to use for this operation.</param>
    /// <returns>True if successful.</returns>
    /// <remarks><seealso href="https://redis.io/commands/vsetattr"/></remarks>
    [Experimental(Experiments.VectorSets, UrlFormat = Experiments.UrlFormat)]
    bool VectorSetSetAttributesJson(
        RedisKey key,
        RedisValue member,
        string jsonAttributes,
        CommandFlags flags = CommandFlags.None);

    /// <summary>
    /// Find similar vectors using vector similarity search.
    /// </summary>
    /// <param name="key">The key of the vectorset.</param>
    /// <param name="query">The query to execute.</param>
    /// <param name="flags">The flags to use for this operation.</param>
    /// <returns>Similar vectors with their similarity scores.</returns>
    /// <remarks><seealso href="https://redis.io/commands/vsim"/></remarks>
    [Experimental(Experiments.VectorSets, UrlFormat = Experiments.UrlFormat)]
    Lease<VectorSetSimilaritySearchResult>? VectorSetSimilaritySearch(
        RedisKey key,
        VectorSetSimilaritySearchRequest query,
        CommandFlags flags = CommandFlags.None);
}
