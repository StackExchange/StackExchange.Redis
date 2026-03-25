extern alias respite;
using respite::RESPite.Messages;
using StackExchange.Redis.Server;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Test Redis server that simulates GCRA rate limiting responses.
/// </summary>
public class GcraTestServer : InProcessTestServer
{
    private readonly GcraRateLimitResult _expectedResult;
    private GcraRequestSnapshot? _lastRequest;

    public GcraTestServer(GcraRateLimitResult expectedResult, ITestOutputHelper? log = null) : base(log)
    {
        _expectedResult = expectedResult;
    }

    /// <summary>
    /// Snapshot of the last GCRA request received by the server.
    /// </summary>
    public sealed class GcraRequestSnapshot
    {
        public RedisKey Key { get; init; }
        public int MaxBurst { get; init; }
        public int RequestsPerPeriod { get; init; }
        public double PeriodSeconds { get; init; }
        public int Count { get; init; }
    }

    /// <summary>
    /// Gets the last GCRA request received by the server.
    /// </summary>
    public GcraRequestSnapshot? LastRequest => _lastRequest;

    /// <summary>
    /// Handles GCRA commands. Returns the configured result and captures request parameters.
    /// </summary>
    [RedisCommand(-5, "GCRA")]
    protected virtual TypedRedisValue Gcra(RedisClient client, in RedisRequest request)
    {
        // Parse request parameters
        var key = request.GetKey(1);
        var maxBurst = request.GetInt32(2);
        var requestsPerPeriod = request.GetInt32(3);
        // Parse period as a string and convert to double
        var periodString = request.GetString(4);
        var periodSeconds = double.Parse(periodString, System.Globalization.CultureInfo.InvariantCulture);

        // Optional count parameter (defaults to 1)
        var count = 1;
        if (request.Count >= 7 && request.GetString(5) == "NUM_REQUESTS")
        {
            count = request.GetInt32(6);
        }

        // Capture the request
        _lastRequest = new GcraRequestSnapshot
        {
            Key = key,
            MaxBurst = maxBurst,
            RequestsPerPeriod = requestsPerPeriod,
            PeriodSeconds = periodSeconds,
            Count = count,
        };

        // Return the configured result as a 5-element array
        var result = TypedRedisValue.Rent(5, out var span, RespPrefix.Array);
        span[0] = TypedRedisValue.Integer(_expectedResult.Limited ? 1 : 0);
        span[1] = TypedRedisValue.Integer(_expectedResult.MaxRequests);
        span[2] = TypedRedisValue.Integer(_expectedResult.AvailableRequests);
        span[3] = TypedRedisValue.Integer(_expectedResult.RetryAfterSeconds);
        span[4] = TypedRedisValue.Integer(_expectedResult.FullBurstAfterSeconds);
        return result;
    }

    /// <summary>
    /// Resets the last request snapshot.
    /// </summary>
    public override void ResetCounters()
    {
        _lastRequest = null;
        base.ResetCounters();
    }
}
