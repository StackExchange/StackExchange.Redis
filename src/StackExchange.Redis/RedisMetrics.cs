#if NET6_0_OR_GREATER

using System.Collections.Generic;
using System.Diagnostics.Metrics;

namespace StackExchange.Redis;

internal sealed class RedisMetrics
{
    private readonly Meter _meter;
    private readonly Counter<long> _operationCount;
    private readonly Counter<long> _completedAsynchronously;
    private readonly Counter<long> _completedSynchronously;
    private readonly Counter<long> _failedAsynchronously;
    private readonly Counter<long> _failedSynchronously;
    private readonly Counter<long> _nonPreferredEndpointCount;

    public static readonly RedisMetrics Instance = new RedisMetrics();

    private RedisMetrics()
    {
        _meter = new Meter("StackExchange.Redis");

        _operationCount = _meter.CreateCounter<long>(
            "redis-operation-count",
            description: "The number of operations performed.");

        _completedAsynchronously = _meter.CreateCounter<long>(
            "redis-completed-asynchronously",
            description: "The number of operations that have been completed asynchronously.");

        _completedSynchronously = _meter.CreateCounter<long>(
            "redis-completed-synchronously",
            description: "The number of operations that have been completed synchronously.");

        _failedAsynchronously = _meter.CreateCounter<long>(
            "redis-failed-asynchronously",
            description: "The number of operations that failed to complete asynchronously.");

        _failedSynchronously = _meter.CreateCounter<long>(
            "redis-failed-synchronously",
            description: "The number of operations that failed to complete synchronously.");

        _nonPreferredEndpointCount = _meter.CreateCounter<long>(
            "redis-non-preferred-endpoint-count",
            description: "Indicates the total number of messages dispatched to a non-preferred endpoint, for example sent to a primary when the caller stated a preference of replica.");
    }

    public void IncrementOperationCount(string endpoint)
    {
        if (_operationCount.Enabled)
        {
            _operationCount.Add(1,
                new KeyValuePair<string, object?>("endpoint", endpoint));
        }
    }

    public void OnMessageComplete(IResultBox? result)
    {
        if (result is not null &&
            (_completedAsynchronously.Enabled ||
            _completedSynchronously.Enabled ||
            _failedAsynchronously.Enabled ||
            _failedSynchronously.Enabled))
        {
            Counter<long> counter = (result.IsFaulted, result.IsAsync) switch
            {
                (false, true) => _completedAsynchronously,
                (false, false) => _completedSynchronously,
                (true, true) => _failedAsynchronously,
                (true, false) => _failedSynchronously,
            };

            // TODO: can we pass endpoint here?
            counter.Add(1);
        }
    }

    public void IncrementNonPreferredEndpointCount(string endpoint)
    {
        if (_nonPreferredEndpointCount.Enabled)
        {
            _nonPreferredEndpointCount.Add(1,
                new KeyValuePair<string, object?>("endpoint", endpoint));
        }
    }
}

#endif
