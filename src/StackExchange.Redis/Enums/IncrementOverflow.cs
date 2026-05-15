using System.Diagnostics.CodeAnalysis;
using RESPite;

namespace StackExchange.Redis;

/// <summary>
/// Describes how an increment operation handles a value that would exceed a configured bound.
/// </summary>
[Experimental(Experiments.Server_8_8, UrlFormat = Experiments.UrlFormat)]
public enum IncrementOverflow
{
    /// <summary>
    /// Return an error when the increment would exceed a configured bound.
    /// </summary>
    Fail,

    /// <summary>
    /// Ignore the operation when the increment would exceed a configured bound.
    /// </summary>
    Reject,

    /// <summary>
    /// Clamp the result to the configured bound when the increment would exceed it.
    /// </summary>
    Saturate,
}
