using System.Diagnostics.CodeAnalysis;
using RESPite;

namespace StackExchange.Redis;

/// <summary>
/// Represents the result of a GCRA (Generic Cell Rate Algorithm) rate limit check.
/// </summary>
[Experimental(Experiments.Server_8_8, UrlFormat = Experiments.UrlFormat)]
public readonly partial struct GcraRateLimitResult
{
    /// <summary>
    /// Indicates whether the token acquisition was rate limited (true) or allowed (false).
    /// </summary>
    public bool Limited { get; }

    /// <summary>
    /// The maximum number of tokens allowed. Always equal to max_burst + 1.
    /// </summary>
    public int MaxTokens { get; }

    /// <summary>
    /// The number of tokens available immediately without being rate limited.
    /// </summary>
    public int AvailableTokens { get; }

    /// <summary>
    /// The number of seconds after which the caller should retry.
    /// Returns -1 if the token acquisition is not limited.
    /// </summary>
    public int RetryAfterSeconds { get; }

    /// <summary>
    /// The number of seconds after which a full burst will be allowed.
    /// </summary>
    public int FullBurstAfterSeconds { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="GcraRateLimitResult"/> struct.
    /// </summary>
    public GcraRateLimitResult(bool limited, int maxTokens, int availableTokens, int retryAfterSeconds, int fullBurstAfterSeconds)
    {
        Limited = limited;
        MaxTokens = maxTokens;
        AvailableTokens = availableTokens;
        RetryAfterSeconds = retryAfterSeconds;
        FullBurstAfterSeconds = fullBurstAfterSeconds;
    }
}
