#if NET6_0_OR_GREATER

using System.Collections.Generic;
using System.Diagnostics.Metrics;

namespace StackExchange.Redis;

internal sealed class RedisMetrics
{
    private readonly Meter _meter;
    private readonly Counter<long> _operationCount;
    //private readonly Counter<long> _completedAsynchronously;
    //private readonly Counter<long> _completedSynchronously;
    //private readonly Counter<long> _failedSynchronously;

    public static readonly RedisMetrics Instance = new RedisMetrics();

    public RedisMetrics()
    {
        _meter = new Meter("StackExchange.Redis");

        _operationCount = _meter.CreateCounter<long>(
            "operation-count",
            description: "The number of operations performed.");

        //_completedAsynchronously = _meter.CreateCounter<long>(
        //    "completed-asynchronously",
        //    description: "The number of operations that have been completed asynchronously.");

        //_completedSynchronously = _meter.CreateCounter<long>(
        //    "completed-synchronously",
        //    description: "The number of operations that have been completed synchronously.");

        //_failedSynchronously = _meter.CreateCounter<long>(
        //    "failed-synchronously",
        //    description: "The number of operations that failed to complete asynchronously.");
    }

    public void IncrementOpCount(string connectionName)
    {
        if (_operationCount.Enabled)
        {
            _operationCount.Add(1,
                new KeyValuePair<string, object?>("connection-name", connectionName));
        }
    }
}

#endif
