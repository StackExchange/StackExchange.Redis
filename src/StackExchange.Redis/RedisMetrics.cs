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
    private readonly Counter<long> _commandsQueued;
    private readonly Counter<long> _commandsSent;
    private readonly Counter<long> _commandsCompleted;
    private readonly Histogram<double> _commandsDuration;
    private readonly Counter<long> _nonPreferredEndpointCount;

    public static readonly RedisMetrics Default = new RedisMetrics();

    public RedisMetrics(Meter? meter = null)
    {
        _meter = meter ?? new Meter("StackExchange.Redis");

        _commandsQueued = _meter.CreateCounter<long>(
            "db.redis.commands.queued",
            description: "The number of commands queued for sending to Redis.");

        _commandsSent = _meter.CreateCounter<long>(
            "db.redis.commands.sent",
            description: "The number of commands sent to Redis.");

        _commandsCompleted = _meter.CreateCounter<long>(
            "db.redis.commands.completed",
            description: "The number of commands completed.");

        _commandsDuration = _meter.CreateHistogram<double>(
            "db.redis.commands.duration",
            unit: "ms",
            description: "Measures the duration of commands requests.");

        _nonPreferredEndpointCount = _meter.CreateCounter<long>(
            "db.redis.non_preferred_endpoint.count",
            description: "Indicates the total number of messages dispatched to a non-preferred endpoint, for example sent to a primary when the caller stated a preference of replica.");
    }
    public void OnCommandQueued(string endpoint) =>
        _commandsQueued.Add(1, new KeyValuePair<string, object?>("endpoint", endpoint));

    [Conditional("NET6_0_OR_GREATER")]
    public void OnCommandSent(string endpoint) =>
        _commandsSent.Add(1, new KeyValuePair<string, object?>("endpoint", endpoint));

    public void OnCommandCompleted(Message message, IResultBox? resultBox, string? endpoint, CommandResult result)
    {
        _commandsCompleted.Add(1,
            new("db.redis.endpoint", endpoint),
            new("db.redis.result", result) // TODO: Boxing
        );

        // The caller ensures we can don't record on the same resultBox from two threads.
        // 'result' can be null if this method is called for the same message more than once.
        if (resultBox is not null && _commandsDuration.Enabled)
        {
            // Stopwatch.GetElapsedTime is only available in net7.0+
            // https://github.com/dotnet/runtime/blob/ae068fec6ede58d2a5b343c5ac41c9ca8715fa47/src/libraries/System.Private.CoreLib/src/System/Diagnostics/Stopwatch.cs#L129-L137
            var now = Stopwatch.GetTimestamp();
            var duration = new TimeSpan((long)((now - message.CreatedTimestamp) * s_tickFrequency));

            _commandsDuration.Record(duration.TotalMilliseconds,
                new("db.redis.async", resultBox.IsAsync ? s_trueBox : s_falseBox),
                new("db.redis.faulted", resultBox.IsFaulted ? s_trueBox : s_falseBox)
            );
        }
    }

    public void IncrementNonPreferredEndpointCount(string endpoint) =>
        _nonPreferredEndpointCount.Add(1, new KeyValuePair<string, object?>("endpoint", endpoint));
}

#endif
