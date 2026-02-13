using System;
using System.Diagnostics.CodeAnalysis;
using RESPite;

namespace StackExchange.Redis;

/// <summary>
/// Represents the request for a vectorset add operation.
/// </summary>
[Experimental(Experiments.VectorSets, UrlFormat = Experiments.UrlFormat)]
public abstract class VectorSetAddRequest
{
    // polymorphism left open for future, but needs to be handled internally
    internal VectorSetAddRequest()
    {
    }

    /// <summary>
    /// Add a member to the vectorset.
    /// </summary>
    /// <param name="element">The element name.</param>
    /// <param name="values">The vector data.</param>
    /// <param name="attributesJson">Optional JSON attributes for the element (SETATTR parameter).</param>
    public static VectorSetAddRequest Member(
        RedisValue element,
        ReadOnlyMemory<float> values,
#if NET7_0_OR_GREATER
        [StringSyntax(StringSyntaxAttribute.Json)]
#endif
        string? attributesJson = null)
        => new VectorSetAddMemberRequest(element, values, attributesJson);

    /// <summary>
    /// Optional check-and-set mode for partial threading (CAS parameter).
    /// </summary>
    public bool UseCheckAndSet { get; set; }

    /// <summary>
    /// Optional dimension reduction using random projection (REDUCE parameter).
    /// </summary>
    public int? ReducedDimensions { get; set; }

    /// <summary>
    /// Quantization type - Int8 (Q8), None (NOQUANT), or Binary (BIN). Default: Int8.
    /// </summary>
    public VectorSetQuantization Quantization { get; set; } = VectorSetQuantization.Int8;

    /// <summary>
    /// Optional HNSW build exploration factor (EF parameter, default: 200).
    /// </summary>
    public int? BuildExplorationFactor { get; set; }

    /// <summary>
    /// Optional maximum connections per HNSW node (M parameter, default: 16).
    /// </summary>
    public int? MaxConnections { get; set; }

    // snapshot the values; I don't trust people not to mutate the object behind my back
    internal abstract VectorSetAddMessage ToMessage(RedisKey key, int db, CommandFlags flags);

    internal sealed class VectorSetAddMemberRequest(
        RedisValue element,
        ReadOnlyMemory<float> values,
        string? attributesJson)
        : VectorSetAddRequest
    {
        internal override VectorSetAddMessage ToMessage(RedisKey key, int db, CommandFlags flags)
            => new VectorSetAddMessage.VectorSetAddMemberMessage(
                db,
                flags,
                key,
                ReducedDimensions,
                Quantization,
                BuildExplorationFactor,
                MaxConnections,
                UseCheckAndSet,
                element,
                values,
                attributesJson);
    }
}
