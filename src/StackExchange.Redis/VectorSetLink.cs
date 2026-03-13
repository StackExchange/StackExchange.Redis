using System.Diagnostics.CodeAnalysis;
using RESPite;

namespace StackExchange.Redis;

/// <summary>
/// Represents a link/connection between members in a vectorset with similarity score.
/// Used by VLINKS command with WITHSCORES option.
/// </summary>
[Experimental(Experiments.VectorSets, UrlFormat = Experiments.UrlFormat)]
public readonly struct VectorSetLink(RedisValue member, double score)
{
    /// <summary>
    /// The linked member name/identifier.
    /// </summary>
    public RedisValue Member { get; } = member;

    /// <summary>
    /// The similarity score between the queried member and this linked member.
    /// </summary>
    public double Score { get; } = score;

    /// <inheritdoc/>
    public override string ToString() => $"{Member}: {Score}";
}
