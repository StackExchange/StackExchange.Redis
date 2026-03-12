namespace StackExchange.Redis;

/// <summary>
/// Represents the result of a GCRA (Generic Cell Rate Algorithm) rate limit check.
/// </summary>
public readonly partial struct GcraRateLimitResult
{
    /// <summary>
    /// Indicates whether the request was rate limited (true) or allowed (false).
    /// </summary>
    public bool Limited { get; }

    /// <summary>
    /// The maximum number of requests allowed. Always equal to max_burst + 1.
    /// </summary>
    public int MaxRequests { get; }

    /// <summary>
    /// The number of requests available immediately without being rate limited.
    /// </summary>
    public int AvailableRequests { get; }

    /// <summary>
    /// The number of seconds after which the caller should retry.
    /// Returns -1 if the request is not limited.
    /// </summary>
    public int RetryAfterSeconds { get; }

    /// <summary>
    /// The number of seconds after which a full burst will be allowed.
    /// </summary>
    public int FullBurstAfterSeconds { get; }

    /// <summary>
    /// Creates a new <see cref="GcraRateLimitResult"/>.
    /// </summary>
    /// <param name="limited">Whether the request was rate limited.</param>
    /// <param name="maxRequests">The maximum number of requests allowed.</param>
    /// <param name="availableRequests">The number of requests available immediately.</param>
    /// <param name="retryAfterSeconds">The number of seconds after which to retry (in seconds). -1 if not limited.</param>
    /// <param name="fullBurstAfterSeconds">The number of seconds after which a full burst will be allowed (in seconds).</param>
    public GcraRateLimitResult(bool limited, int maxRequests, int availableRequests, int retryAfterSeconds, int fullBurstAfterSeconds)
    {
        Limited = limited;
        MaxRequests = maxRequests;
        AvailableRequests = availableRequests;
        RetryAfterSeconds = retryAfterSeconds;
        FullBurstAfterSeconds = fullBurstAfterSeconds;
    }
}
