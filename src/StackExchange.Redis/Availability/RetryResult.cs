using System;
using System.Diagnostics.CodeAnalysis;
using RESPite;

namespace StackExchange.Redis.Availability;

/// <summary>
/// Indicates the result of a <see cref="RetryPolicy"/> query.
/// </summary>
[Flags]
[Experimental(Experiments.GeoRedundantFailover, UrlFormat = Experiments.UrlFormat)]
public enum RetryResult
{
    /// <summary>
    /// None; the operation should not be retried.
    /// </summary>
    None = 0,

    /// <summary>
    /// The operation can be retried on the same server.
    /// </summary>
    SameServer = 1,

    /// <summary>
    /// The operation can be retried on a different server after a failover operation.
    /// </summary>
    FailoverServer = 2,
}
