using System;
using System.Diagnostics.CodeAnalysis;
using RESPite;

namespace StackExchange.Redis.Availability;

/// <summary>
/// Describes the target of a single health-check probe, and the budget available to it.
/// </summary>
/// <remarks>
/// This is passed by value rather than by <c>in</c>, because probe implementations are
/// typically <c>async</c>, and async methods cannot take by-ref parameters.
/// </remarks>
[Experimental(Experiments.GeoRedundantFailover, UrlFormat = Experiments.UrlFormat)]
public readonly struct HealthCheckContext(IServer server, TimeSpan probeTimeout)
{
    /// <inheritdoc/>
    public override string ToString() => $"{Server?.EndPoint} (timeout: {ProbeTimeout})";

    /// <summary>
    /// Gets the server being probed.
    /// </summary>
    public IServer Server => server;

    /// <summary>
    /// Gets the time allowed for this probe to complete; probes are not required to enforce this
    /// themselves (the caller applies it), but may use it to bound any state they create.
    /// </summary>
    public TimeSpan ProbeTimeout => probeTimeout;
}
