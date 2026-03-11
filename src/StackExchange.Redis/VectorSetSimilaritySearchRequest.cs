using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using RESPite;
using VsimFlags = StackExchange.Redis.VectorSetSimilaritySearchMessage.VsimFlags;

namespace StackExchange.Redis;

/// <summary>
/// Represents the request for a vector similarity search operation.
/// </summary>
[Experimental(Experiments.VectorSets, UrlFormat = Experiments.UrlFormat)]
public abstract class VectorSetSimilaritySearchRequest
{
    internal VectorSetSimilaritySearchRequest()
    {
    } // polymorphism left open for future, but needs to be handled internally

    private sealed class VectorSetSimilarityByMemberSearchRequest(RedisValue member) : VectorSetSimilaritySearchRequest
    {
        internal override VectorSetSimilaritySearchMessage ToMessage(RedisKey key, int db, CommandFlags flags)
            => new VectorSetSimilaritySearchMessage.VectorSetSimilaritySearchByMemberMessage(
                db,
                flags,
                _vsimFlags,
                key,
                member,
                _count,
                _epsilon,
                _searchExplorationFactor,
                _filterExpression,
                _maxFilteringEffort);
    }

    private sealed class VectorSetSimilarityVectorSingleSearchRequest(ReadOnlyMemory<float> vector)
        : VectorSetSimilaritySearchRequest
    {
        internal override VectorSetSimilaritySearchMessage ToMessage(RedisKey key, int db, CommandFlags flags)
            => new VectorSetSimilaritySearchMessage.VectorSetSimilaritySearchBySingleVectorMessage(
                db,
                flags,
                _vsimFlags,
                key,
                vector,
                _count,
                _epsilon,
                _searchExplorationFactor,
                _filterExpression,
                _maxFilteringEffort);
    }

    // snapshot the values; I don't trust people not to mutate the object behind my back
    internal abstract VectorSetSimilaritySearchMessage ToMessage(RedisKey key, int db, CommandFlags flags);

    /// <summary>
    /// Create a request to search by an existing member in the index.
    /// </summary>
    /// <param name="member">The member to search for.</param>
    public static VectorSetSimilaritySearchRequest ByMember(RedisValue member)
        => new VectorSetSimilarityByMemberSearchRequest(member);

    /// <summary>
    /// Create a request to search by a vector value.
    /// </summary>
    /// <param name="vector">The vector value to search for.</param>
    public static VectorSetSimilaritySearchRequest ByVector(ReadOnlyMemory<float> vector)
        => new VectorSetSimilarityVectorSingleSearchRequest(vector);

    private VsimFlags _vsimFlags;

    // use the flags to reduce storage from N*Nullable<T>
    private int _searchExplorationFactor, _maxFilteringEffort, _count;
    private double _epsilon;

    private bool HasFlag(VsimFlags flag) => (_vsimFlags & flag) != 0;

    private void SetFlag(VsimFlags flag, bool value)
    {
        if (value)
        {
            _vsimFlags |= flag;
        }
        else
        {
            _vsimFlags &= ~flag;
        }
    }

    /// <summary>
    /// The number of similar vectors to return (COUNT parameter).
    /// </summary>
    public int? Count
    {
        get => HasFlag(VsimFlags.Count) ? _count : null;
        set
        {
            if (value.HasValue)
            {
                _count = value.GetValueOrDefault();
                SetFlag(VsimFlags.Count, true);
            }
            else
            {
                SetFlag(VsimFlags.Count, false);
            }
        }
    }

    /// <summary>
    /// Whether to include similarity scores in the results (WITHSCORES parameter).
    /// </summary>
    public bool WithScores
    {
        get => HasFlag(VsimFlags.WithScores);
        set => SetFlag(VsimFlags.WithScores, value);
    }

    /// <summary>
    /// Whether to include JSON attributes in the results (WITHATTRIBS parameter).
    /// </summary>
    public bool WithAttributes
    {
        get => HasFlag(VsimFlags.WithAttributes);
        set => SetFlag(VsimFlags.WithAttributes, value);
    }

    /// <summary>
    /// Optional similarity threshold - only return elements with similarity >= (1 - epsilon) (EPSILON parameter).
    /// </summary>
    public double? Epsilon
    {
        get => HasFlag(VsimFlags.Epsilon) ? _epsilon : null;
        set
        {
            if (value.HasValue)
            {
                _epsilon = value.GetValueOrDefault();
                SetFlag(VsimFlags.Epsilon, true);
            }
            else
            {
                SetFlag(VsimFlags.Epsilon, false);
            }
        }
    }

    /// <summary>
    /// Optional search exploration factor for better recall (EF parameter).
    /// </summary>
    public int? SearchExplorationFactor
    {
        get => HasFlag(VsimFlags.SearchExplorationFactor) ? _searchExplorationFactor : null;
        set
        {
            if (value.HasValue)
            {
                _searchExplorationFactor = value.GetValueOrDefault();
                SetFlag(VsimFlags.SearchExplorationFactor, true);
            }
            else
            {
                SetFlag(VsimFlags.SearchExplorationFactor, false);
            }
        }
    }

    /// <summary>
    /// Optional maximum filtering attempts (FILTER-EF parameter).
    /// </summary>
    public int? MaxFilteringEffort
    {
        get => HasFlag(VsimFlags.MaxFilteringEffort) ? _maxFilteringEffort : null;
        set
        {
            if (value.HasValue)
            {
                _maxFilteringEffort = value.GetValueOrDefault();
                SetFlag(VsimFlags.MaxFilteringEffort, true);
            }
            else
            {
                SetFlag(VsimFlags.MaxFilteringEffort, false);
            }
        }
    }

    private string? _filterExpression;

    /// <summary>
    /// Optional filter expression to restrict results (FILTER parameter); <see href="https://redis.io/docs/latest/develop/data-types/vector-sets/filtered-search/"/>.
    /// </summary>
    public string? FilterExpression
    {
        get => _filterExpression;
        set
        {
            _filterExpression = value;
            SetFlag(VsimFlags.FilterExpression, !string.IsNullOrWhiteSpace(value));
        }
    }

    /// <summary>
    /// Whether to use exact linear scan instead of HNSW (TRUTH parameter).
    /// </summary>
    public bool UseExactSearch
    {
        get => HasFlag(VsimFlags.UseExactSearch);
        set => SetFlag(VsimFlags.UseExactSearch, value);
    }

    /// <summary>
    /// Whether to run search in main thread (NOTHREAD parameter).
    /// </summary>
    [Browsable(false), EditorBrowsable(EditorBrowsableState.Advanced)]
    public bool DisableThreading
    {
        get => HasFlag(VsimFlags.DisableThreading);
        set => SetFlag(VsimFlags.DisableThreading, value);
    }
}
