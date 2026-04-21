using System.Diagnostics.CodeAnalysis;
using RESPite;

namespace StackExchange.Redis;

/// <summary>
/// Determines how a stream message is negatively acknowledged back to the consumer group.
/// </summary>
[Experimental(Experiments.Server_8_8, UrlFormat = Experiments.UrlFormat)]
public enum StreamNackMode
{
    /// <summary>
    /// Release the message without counting it as an additional failure.
    /// </summary>
    Silent = 0,

    /// <summary>
    /// Release the message and treat it as a normal failed delivery.
    /// </summary>
    Fail = 1,

    /// <summary>
    /// Release the message and mark it as a terminal failure.
    /// </summary>
    Fatal = 2,
}
