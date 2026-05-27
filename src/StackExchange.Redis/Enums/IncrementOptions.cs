using System;
using System.Diagnostics.CodeAnalysis;
using RESPite;

namespace StackExchange.Redis;

/// <summary>
/// Describes options for increment operations.
/// </summary>
[Experimental(Experiments.Server_8_8, UrlFormat = Experiments.UrlFormat)]
[Flags]
public enum IncrementOptions
{
    /// <summary>
    /// No additional options. Out-of-bounds increments are rejected by the server without applying the increment.
    /// </summary>
    None = 0,

    /// <summary>
    /// Clamp the result to the configured bound when the increment would exceed it.
    /// </summary>
    Saturate = 1,
}
