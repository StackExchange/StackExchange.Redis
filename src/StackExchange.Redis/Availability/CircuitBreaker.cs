using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;

namespace StackExchange.Redis.Availability;

public abstract class CircuitBreaker
{
    public class Builder
    {
        /// <summary>
        /// Percentage of failures to trigger circuit breaker.
        /// </summary>
        /// <remarks>Failures are only included if they are of tracked exception types.</remarks>
        public double FailureRateThreshold { get; set; } = 10;

        /// <summary>
        /// Minimum failures before circuit breaker can open.
        /// </summary>
        public int MinimumNumberOfFailures { get; set; } = 1000;

        /// <summary>
        /// Time window for collecting metrics.
        /// </summary>
        public TimeSpan MetricsWindowSize { get; set; } = TimeSpan.FromSeconds(2);

        /// <summary>
        /// Create a new circuit-breaker instance.
        /// </summary>
        public CircuitBreaker Create() => new DefaultCircuitBreaker(
            FailureRateThreshold, MinimumNumberOfFailures, MetricsWindowSize, TrackedExceptions);

        /// <summary>
        /// Create a new circuit-breaker instance.
        /// </summary>
        public static implicit operator CircuitBreaker(Builder builder) => builder.Create();

        /// <summary>
        /// Exceptions that count as failures.
        /// </summary>
        public ImmutableArray<Type> TrackedExceptions { get; set; } = TrackedExceptionsDefault;
    }

    // important: if this changes, update the _isDefaultExceptions logic
    private static readonly ImmutableArray<Type> TrackedExceptionsDefault =
        new[] { typeof(RedisConnectionException), typeof(RedisTimeoutException) }.ToImmutableArray();

    /// <summary>
    /// Create an object to collate observations for a connection.
    /// </summary>
    public abstract Accumulator CreateAccumulator();

    /// <summary>
    /// Collates observations for a connection.
    /// </summary>
    public abstract class Accumulator
    {
        /// <summary>
        /// Respond to a message outcome, and indicate whether the connection is considered healthy.
        /// </summary>
        /// <returns>True if the connection is still considered healthy.</returns>
        public abstract bool IsHealthy(in CircuitBreakerContext context);
    }

    /// <summary>
    /// Provides information about a circuit-breaker test.
    /// </summary>
    public readonly struct CircuitBreakerContext(bool success, Exception? fault)
    {
        /// <summary>
        /// Was the operation a success.
        /// </summary>
        public bool Success => success;

        /// <summary>
        /// The fault associated with the operation.
        /// </summary>
        public Exception? Fault => fault;
    }

    private enum ExceptionStrategy
    {
        Default,
        Any,
        None,
        CustomOpen,
        CustomSealed,
    }

    private sealed class DefaultCircuitBreaker : CircuitBreaker
    {
        private readonly ExceptionStrategy _trackingStrategy;
        private readonly ImmutableArray<Type> _trackedExceptions;
        private readonly double _failureRateThreshold;
        private readonly int _minimumNumberOfFailures;

        // the metrics window is divided into a fixed number of equal time-slices ("buckets");
        // this lets us keep a rolling count cheaply, evicting whole buckets as they age out,
        // rather than storing (and pruning) a timestamp per event
        private const int BucketCount = 10;
        private readonly long _bucketTicks; // width of one bucket, in Stopwatch ticks

        public DefaultCircuitBreaker(
            double failureRateThreshold,
            int minimumNumberOfFailures,
            TimeSpan metricsWindowSize,
            ImmutableArray<Type> trackedExceptions)
        {
            _trackedExceptions = CheckExceptions(trackedExceptions, out _trackingStrategy);
            _failureRateThreshold = failureRateThreshold;
            _minimumNumberOfFailures = minimumNumberOfFailures;

            long windowTicks = (long)(metricsWindowSize.TotalSeconds * Stopwatch.Frequency);
            _bucketTicks = Math.Max(1, windowTicks / BucketCount);
        }

        public override Accumulator CreateAccumulator() => new DefaultAccumulator(this);

        private sealed class DefaultAccumulator(DefaultCircuitBreaker breaker) : Accumulator
        {
            // ring of buckets; each holds the counts for a single time-slice, tagged with the
            // slice ("epoch") it currently represents so we can tell live buckets from stale ones
#if NET8_0_OR_GREATER
            // inline it directly into the accumulator
            private BucketRing _buckets; // cannot be "readonly", else the indexer is "ref readonly"
            [InlineArray(BucketCount)]
            private struct BucketRing
            {
                private Bucket _element0;
            }
#else
            // fallback to a separate heap array
            private readonly Bucket[] _buckets = new Bucket[BucketCount];
#endif

            private struct Bucket
            {
                private long _epoch;
                private volatile int _success, _failure;

                // note that Volatile guarantees atomicity (even on x86), so no torn values here
                // see https://learn.microsoft.com/dotnet/api/system.threading.volatile
                public long Epoch => Volatile.Read(ref _epoch);

                public int Success => _success;
                public int Failure => _failure;

                public void Count(long epoch, bool success)
                {
                    if (epoch != Epoch)
                    {
                        // epoch rollover; clear the counts first, to prevent anyone over-counting
                        // in their count loop (under-counting is fine); dropped counts are self-correcting
                        // if there's an actual problem, stale data misread as current: is not.
                        _success = _failure = 0;
                        Volatile.Write(ref _epoch, epoch);

                        // if we want to get *super* accurate, we could use bit-packing here and use
                        // CAS over a 64-bit value, but we'd need to compromise on count upper bounds
                        // *and* we'd need to consider the max epoch problem - maybe 32 bits for epoch
                        // and 16 bits for each count, but... let's keep things simple and accept a
                        // few dropped counts instead, and luxuriate in unreasonably fat epochs and counts.
                    }
                    // ReSharper disable ByRefArgumentIsVolatileField
                    Interlocked.Increment(ref success ? ref _success : ref _failure);
                    // ReSharper restore ByRefArgumentIsVolatileField
                }
            }

            public override bool IsHealthy(in CircuitBreakerContext context)
            {
                // not-tracked failures (based on exception type) count as "success" for the purposes of circuit breaking
                bool countAsSuccess = context.Success || !breaker.IsTracked(context.Fault);

                // which time-slice are we in, and where does it live in the ring?
                long epoch = Stopwatch.GetTimestamp() / breaker._bucketTicks;
                int index = (int)(epoch % BucketCount);

                // note: to avoid concurrency problems, we're going lock-free here; *technically* this
                // might mean we see race oddities during epoch rollovers, but in general that means we're
                // miscounting by a tiny amount during intervals when there's enough load to get a race,
                // in which case: we're probably fine; also, note that when tracking by endpoint, we're
                // only getting results one at a time, so we shouldn't be over-stomping much *anyway*
                ref Bucket bucket = ref _buckets[index];
                bucket.Count(epoch, countAsSuccess);

                // sum only the buckets still inside the window; anything older is ignored
                // (and contributes nothing even if left un-recycled). empty/never-used buckets
                // add zero, so no explicit "unused" sentinel is required
                long oldest = epoch - BucketCount + 1;
                int failures, total = failures = 0;
                foreach (ref readonly Bucket b in _buckets)
                {
                    if (b.Epoch < oldest) continue;

                    int failure;
                    failures += failure = b.Failure; // capture to avoid double-read (think epoch rollover)
                    total += b.Success + failure;
                }

                // don't act until we've seen enough failures to be statistically meaningful
                if (total is 0 | failures < breaker._minimumNumberOfFailures)
                {
                    return true;
                }
                double failureRate = (failures * 100d) / total;
                return failureRate < breaker._failureRateThreshold;
            }
        }

        private static ImmutableArray<Type> CheckExceptions(ImmutableArray<Type> tracked, out ExceptionStrategy strategy)
        {
            if (tracked.IsDefaultOrEmpty)
            {
                strategy = ExceptionStrategy.None;
                return default;
            }
            strategy = ExceptionStrategy.CustomOpen;

            if (tracked.Length is 1 && tracked[0] == typeof(Exception))
            {
                strategy = ExceptionStrategy.Any;
                return default;
            }

            if (tracked.Equals(TrackedExceptionsDefault)) // identity equality
            {
                strategy = ExceptionStrategy.Default;
                return default;
            }
            if (tracked.Length == TrackedExceptionsDefault.Length) // semantic equality
            {
                strategy = ExceptionStrategy.Default;
                // iterate defaultTrackedExceptions, because we know it isn't duplicated
                foreach (var exception in TrackedExceptionsDefault.AsSpan())
                {
                    if (!tracked.Contains(exception))
                    {
                        strategy = ExceptionStrategy.CustomOpen;
                        break;
                    }
                }
                if (strategy is ExceptionStrategy.Default)
                {
                    return default;
                }
            }

            // finally, see if they're all sealed types
            strategy = ExceptionStrategy.CustomSealed;
            foreach (var exception in tracked)
            {
                if (!exception.IsSealed)
                {
                    strategy = ExceptionStrategy.CustomOpen;
                    break;
                }
            }
            return tracked;
        }

        private bool IsTracked(Exception? fault)
        {
            if (fault is null) return false;
            switch (_trackingStrategy)
            {
                case ExceptionStrategy.Any:
                    return true;
                case ExceptionStrategy.Default:
                    return fault is RedisTimeoutException or RedisConnectionException;
                case ExceptionStrategy.None:
                    return false;
            }

            var span = _trackedExceptions.AsSpan();
            var actualType = fault.GetType();
            // check for exact matches
            foreach (var testType in span)
            {
                if (ReferenceEquals(testType, actualType)) return true;
            }

            if (_trackingStrategy is ExceptionStrategy.CustomOpen)
            {
                // we need to check for subclasses (more expensive)
                foreach (var testType in span)
                {
                    if (!testType.IsSealed && testType.IsAssignableFrom(actualType)) return true;
                }
            }

            return false;
        }
    }
}
