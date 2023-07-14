#if NET6_0_OR_GREATER

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace StackExchange.Redis;

internal sealed class RedisMetrics
{
    private static readonly double s_tickFrequency = (double)TimeSpan.TicksPerSecond / Stopwatch.Frequency;
    // cache these boxed boolean values so we don't allocate on each usage.
    private static readonly object s_trueBox = true;
    private static readonly object s_falseBox = false;

    private readonly Meter _meter;
    private readonly Counter<long> _operationCount;
    private readonly Histogram<double> _messageDuration;
    private readonly Counter<long> _nonPreferredEndpointCount;

    public static readonly RedisMetrics Instance = new RedisMetrics();

    private RedisMetrics()
    {
        _meter = new Meter("StackExchange.Redis");

        _operationCount = _meter.CreateCounter<long>(
            "db.redis.operation.count",
            description: "The number of operations performed.");

        _messageDuration = _meter.CreateHistogram<double>(
            "db.redis.duration",
            unit: "s",
            description: "Measures the duration of outbound message requests.");

        _nonPreferredEndpointCount = _meter.CreateCounter<long>(
            "db.redis.non_preferred_endpoint.count",
            description: "Indicates the total number of messages dispatched to a non-preferred endpoint, for example sent to a primary when the caller stated a preference of replica.");
    }

    public void IncrementOperationCount(string endpoint)
    {
        _operationCount.Add(1,
            new KeyValuePair<string, object?>("endpoint", endpoint));
    }

    public void OnMessageComplete(Message message, IResultBox? result)
    {
        // The caller ensures we can don't record on the same resultBox from two threads.
        // 'result' can be null if this method is called for the same message more than once.
        if (result is not null && _messageDuration.Enabled)
        {
            // Stopwatch.GetElapsedTime is only available in net7.0+
            // https://github.com/dotnet/runtime/blob/ae068fec6ede58d2a5b343c5ac41c9ca8715fa47/src/libraries/System.Private.CoreLib/src/System/Diagnostics/Stopwatch.cs#L129-L137
            var now = Stopwatch.GetTimestamp();
            var duration = new TimeSpan((long)((now - message.CreatedTimestamp) * s_tickFrequency));

            var tags = new TagList
            {
                { "db.redis.async", result.IsAsync ? s_trueBox : s_falseBox },
                { "db.redis.faulted", result.IsFaulted ? s_trueBox : s_falseBox }
                // TODO: can we pass endpoint here?
                // should we log the Db?
                // { "db.redis.database_index", message.Db },
            };

            _messageDuration.Record(duration.TotalSeconds, tags);
        }
    }

    public void IncrementNonPreferredEndpointCount(string endpoint)
    {
        _nonPreferredEndpointCount.Add(1,
            new KeyValuePair<string, object?>("endpoint", endpoint));
    }
}

#endif
