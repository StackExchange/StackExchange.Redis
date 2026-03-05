using System.Diagnostics.CodeAnalysis;

namespace StackExchange.Redis;

/// <summary>
/// Configuration parameters for a stream, for example idempotent producer (IDMP) duration and maxsize.
/// </summary>
[Experimental(Experiments.Server_8_6, UrlFormat = Experiments.UrlFormat)]
public sealed class StreamConfiguration
{
    /// <summary>
    /// How long the server remembers each iid, in seconds.
    /// </summary>
    public long? IdmpDuration { get; set; }

    /// <summary>
    /// Maximum number of iids the server remembers per pid.
    /// </summary>
    public long? IdmpMaxSize { get; set; }
}
