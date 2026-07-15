using System;
using System.Threading;

namespace StackExchange.Redis.Availability;

/// <summary>
/// Configures how messages can be retried due to connection / transient faults. Other faults (such as invalid
/// usage) are not retried.
/// </summary>
public class RetryPolicy
{
    /// <summary>
    /// Controls (via <see cref="CircuitBreaker.IsConnectionFault"/>) which faults can be retried; if not supplied,
    /// the connection's configured <see cref="CircuitBreaker"/> will be used.
    /// </summary>
    public CircuitBreaker? CircuitBreaker { get; set; }

    /// <summary>
    /// The maximum number of times an operation can be attempted. Defaults to 3.
    /// </summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>
    /// The maximum number of times to retry an operation before waiting for failover; this only currently
    /// applies to multi-group connections created via <c>ConnectionMultiplexer.ConnectGroupAsync</c>.
    /// Defaults to 1.
    /// </summary>
    public int MaxAttemptsBeforeFailover { get; set; } = 1;

    private int _delayMillis = 1000, _jitterMillis = 500, _failoverMillis = 5000;

    /// <summary>
    /// Gets the time to wait between retries that are *not* dependent on a failover happening. Defaults to 1 second.
    /// </summary>
    public TimeSpan RetryDelay
    {
        get => TimeSpan.FromMilliseconds(_delayMillis);
        set => _delayMillis = checked((int)value.TotalMilliseconds);
    }

    /// <summary>
    /// Gets the time to wait for a failover, after <see cref="MaxAttemptsBeforeFailover"/> attempts. Only one
    /// failover attempt is awaited. When this applies, <see cref="RetryDelay"/> is ignored,
    /// but <see cref="JitterMax"/> is still respected. Defaults to 5 seconds.
    /// </summary>
    public TimeSpan FailoverDelay
    {
        get => TimeSpan.FromMilliseconds(_failoverMillis);
        set => _failoverMillis = checked((int)value.TotalMilliseconds);
    }

    /// <summary>
    /// Gets or sets the upper bound for jitter - additional random delay between retries to prevent stampedes.
    /// Defaults to 0.5 seconds, meaning between 0 and 0.5 *additional* seconds on top of <see cref="RetryDelay"/>.
    /// </summary>
    public TimeSpan JitterMax
    {
        get => TimeSpan.FromMilliseconds(_jitterMillis);
        set => _jitterMillis = checked((int)value.TotalMilliseconds);
    }

    internal int DelayMilliseconds => _delayMillis;
    internal int JitterMilliseconds => _jitterMillis;
    internal int FailoverMilliseconds => _failoverMillis;
}
