using System;
using System.Net;
using System.Text;

namespace StackExchange.Redis;

/// <summary>
/// A set of related endpoints.
/// </summary>
public sealed class EndPointGroup
{
    internal const double DefaultWeight = 1.0;

    /// <summary>
    /// The relative weight (priority) of this group.
    /// </summary>
    public double Weight { get; set; } = DefaultWeight;

    internal bool IsDefaultWeight => Math.Abs(Weight - DefaultWeight) < 0.0001;

    /// <summary>
    /// Gets the endpoints in this group.
    /// </summary>
    public EndPointCollection EndPoints { get; }
    internal EndPointGroup? Next;

    internal EndPointGroup(EndPointCollection? endpoints = null)
    {
        EndPoints = endpoints ?? new();
    }

    /// <inheritdoc />
    public override string ToString()
    {
        var endpoints = EndPoints;
        if (endpoints.Count == 0) return ""; // weight makes no sense without endpoints

        var sb = new StringBuilder();
        sb.Append(Format.ToString(Weight));
        foreach (var endpoint in endpoints)
        {
            sb.Append('|').Append(Format.ToString(endpoint));
        }

        return sb.ToString();
    }
}
