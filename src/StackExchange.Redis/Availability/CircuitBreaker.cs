using System;
#if !NET8_0_OR_GREATER
using System.Diagnostics;
#endif
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using RESPite;

namespace StackExchange.Redis.Availability;

/// <summary>
/// Reports connection health by responding to observed success and failure conditions of processed messages.
/// </summary>
[Experimental(Experiments.ActiveActive, UrlFormat = Experiments.UrlFormat)]
public abstract class CircuitBreaker
{
    /// <summary>
    /// Default circuit-breaker logic.
    /// </summary>
    public static CircuitBreaker Default => Builder.DefaultInstance;

    /// <summary>
    /// No circuit-breaker logic is applied.
    /// </summary>
    public static CircuitBreaker None => NulCircuitBreaker.Instance;

    /// <summary>
    /// Indicates a fault that should be handled by the circuit-breaker or related retry logic.
    /// </summary>
    public virtual bool IsConnectionFault(Exception? fault)
        => fault is RedisTimeoutException or RedisConnectionException;

    /// <summary>
    /// Allows configuration of the default <see cref="CircuitBreaker"/> implementation.
    /// </summary>
    public class Builder
    {
        private const double DefaultFailureRateThreshold = 10;
        private const int DefaultMinimumNumberOfFailures = 1000;
        private static readonly TimeSpan DefaultMetricsWindowSize = TimeSpan.FromSeconds(2);

        internal static CircuitBreaker DefaultInstance = new DefaultCircuitBreaker(
            DefaultFailureRateThreshold,
            DefaultMinimumNumberOfFailures,
            DefaultMetricsWindowSize,
#if NET8_0_OR_GREATER
            timeProvider: null,
#endif
            trackedExceptions: null);

        /// <summary>
        /// Percentage of failures to trigger circuit breaker.
        /// </summary>
        /// <remarks>Failures are only included if they are of tracked exception types.</remarks>
        public double FailureRateThreshold { get; set; } = DefaultFailureRateThreshold;

        /// <summary>
        /// Minimum failures before circuit breaker can open.
        /// </summary>
        public int MinimumNumberOfFailures { get; set; } = DefaultMinimumNumberOfFailures;

        /// <summary>
        /// Time window for collecting metrics.
        /// </summary>
        public TimeSpan MetricsWindowSize { get; set; } = DefaultMetricsWindowSize;

#if NET8_0_OR_GREATER
        /// <summary>
        /// Time source used to drive the metrics window; when null, the system clock is used.
        /// Intended for testing, to make the time-windowed logic deterministic.
        /// </summary>
        internal TimeProvider? TimeProvider { get; set; }
#endif

        /// <summary>
        /// Create a new circuit-breaker instance.
        /// </summary>
        public CircuitBreaker Create()
        {
            if ((FailureRateThreshold is DefaultFailureRateThreshold
                 & MinimumNumberOfFailures is DefaultMinimumNumberOfFailures
#if NET8_0_OR_GREATER
                 & TimeProvider is null
#endif
                 & TrackedExceptions is null)
                && MetricsWindowSize == DefaultMetricsWindowSize)
                return DefaultInstance;

            return new DefaultCircuitBreaker(
                FailureRateThreshold,
                MinimumNumberOfFailures,
                MetricsWindowSize,
#if NET8_0_OR_GREATER
                TimeProvider,
#endif
                TrackedExceptions);
        }

        /// <summary>
        /// Create a new circuit-breaker instance.
        /// </summary>
        public static implicit operator CircuitBreaker(Builder builder) => builder.Create();

        /// <summary>
        /// Exceptions that count as failures. When null, <see cref="RedisConnectionException"/>
        /// and <see cref="RedisTimeoutException"/> are assumed.
        /// </summary>
        public Type[]? TrackedExceptions { get; set; }
    }

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
        /// Record a message outcome, and indicate whether the connection is considered healthy.
        /// </summary>
        /// <returns>True if the connection is still considered healthy.</returns>
        /// struct arg here is in case we want to add more things later
        public abstract bool ObserveResult(in CircuitBreakerContext context);

        internal bool ObserveResult(Exception? fault)
        {
            // only evaluate state upon failure; don't pay that overhead for success, just increment the counters
            bool evaluate = fault is not null;
            var ctx = new CircuitBreakerContext(fault, evaluate: evaluate);
            bool healthy = ObserveResult(in ctx);

            // when we didn't ask for an evaluation, the returned verdict is meaningless: a custom
            // implementation might return default(false) without having actually computed anything.
            // never treat a non-evaluating observation as unhealthy - only a genuine evaluation may trip.
            return !evaluate || healthy;
        }

        /// <summary>
        /// Indicate whether the connection is currently considered healthy, without recording an observation.
        /// </summary>
        /// <returns>True if the connection is considered healthy.</returns>
        public abstract bool IsHealthy();

        /// <summary>
        /// Discard all accumulated observations, returning to a clean state.
        /// </summary>
        public abstract void Reset();
    }

    /// <summary>
    /// Provides information about a circuit-breaker test.
    /// </summary>
    public readonly struct CircuitBreakerContext(Exception? fault, bool evaluate = true)
    {
        /// <summary>
        /// Was the operation a success.
        /// </summary>
        [MemberNotNullWhen(false, nameof(Fault))]
        public bool Success => fault is null;

        /// <summary>
        /// The fault associated with the operation.
        /// </summary>
        public Exception? Fault => fault;

        internal bool Evaluate => evaluate;
    }

    private enum ExceptionStrategy
    {
        Default,
        Any,
        None,
        CustomOpen,
        CustomSealed,
    }

    private sealed class NulCircuitBreaker : CircuitBreaker
    {
        public static readonly NulCircuitBreaker Instance = new();
        private NulCircuitBreaker() { }
        public override Accumulator CreateAccumulator() => NulAccumulator.AccumulatorInstance;

        private sealed class NulAccumulator : Accumulator
        {
            public static readonly NulAccumulator AccumulatorInstance = new();
            private NulAccumulator() { }
            public override bool ObserveResult(in CircuitBreakerContext context) => true;
            public override bool IsHealthy() => true;
            public override void Reset() { }
        }

        // note we leave IsConnectionFault alone - that would impact RetryDatabase, where this is the key
    }
    private sealed class DefaultCircuitBreaker : CircuitBreaker
    {
        private readonly ExceptionStrategy _trackingStrategy;
        private readonly Type[] _trackedExceptions;
        private readonly double _failureRateThreshold;
        private readonly int _minimumNumberOfFailures;

        // the metrics window is divided into a fixed number of equal time-slices ("buckets");
        // this lets us keep a rolling count cheaply, evicting whole buckets as they age out,
        // rather than storing (and pruning) a timestamp per event
        private const int BucketCount = 10;
        private readonly long _bucketTicks; // width of one bucket, in high-resolution ticks

#if NET8_0_OR_GREATER
        private readonly TimeProvider _time;
#endif

        // which time-slice ("epoch") are we in right now: the high-resolution timestamp (from
        // TimeProvider where available, so tests can drive it; otherwise the Stopwatch clock)
        // divided down to bucket width
        private long GetEpoch() =>
#if NET8_0_OR_GREATER
            _time.GetTimestamp()
#else
            Stopwatch.GetTimestamp()
#endif
            / _bucketTicks;

        public DefaultCircuitBreaker(
            double failureRateThreshold,
            int minimumNumberOfFailures,
            TimeSpan metricsWindowSize,
#if NET8_0_OR_GREATER
            TimeProvider? timeProvider,
#endif
            Type[]? trackedExceptions)
        {
            _trackedExceptions = CheckExceptions(trackedExceptions, out _trackingStrategy);
            _failureRateThreshold = failureRateThreshold;
            _minimumNumberOfFailures = minimumNumberOfFailures;

#if NET8_0_OR_GREATER
            _time = timeProvider ?? TimeProvider.System;
            long frequency = _time.TimestampFrequency;
#else
            long frequency = Stopwatch.Frequency;
#endif
            long windowTicks = (long)(metricsWindowSize.TotalSeconds * frequency);
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
                // beware init rules; we're OK in this case because _buckets is in a field on a heap object,
                // but if BucketRing was ever used as a local: [SkipLocalsInit] rules can apply.
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

                public void Reset()
                {
                    // clear the counts first (as in Count), so a concurrent count-loop reader never
                    // attributes these stale counts to the epoch; then blank the epoch back to "unused"
                    _success = _failure = 0;
                    Volatile.Write(ref _epoch, 0);
                }
            }

            public override bool ObserveResult(in CircuitBreakerContext context)
            {
                // not-tracked failures (based on exception type) count as "success" for the purposes of circuit breaking
                bool countAsSuccess = context.Success || !breaker.IsConnectionFault(context.Fault);

                // which time-slice are we in, and where does it live in the ring?
                long epoch = breaker.GetEpoch();
                int index = (int)(epoch % BucketCount);

                // note: to avoid concurrency problems, we're going lock-free here; *technically* this
                // might mean we see race oddities during epoch rollovers, but in general that means we're
                // miscounting by a tiny amount during intervals when there's enough load to get a race,
                // in which case: we're probably fine; also, note that when tracking by endpoint, we're
                // only getting results one at a time, so we shouldn't be over-stomping much *anyway*
                Span<Bucket> buckets = _buckets; // *not* a payload copy; this is in-place over the data
                ref Bucket bucket = ref buckets[index];
                bucket.Count(epoch, countAsSuccess);

                return !context.Evaluate || Evaluate(epoch);
            }

            public override bool IsHealthy() => Evaluate(breaker.GetEpoch());

            // evaluate health from the buckets still inside the window ending at the given epoch,
            // without recording anything; shared by ObserveResult and IsHealthy
            private bool Evaluate(long epoch)
            {
                // sum only the buckets still inside the window; anything older is ignored
                // (and contributes nothing even if left un-recycled). empty/never-used buckets
                // add zero, so no explicit "unused" sentinel is required
                long oldest = epoch - BucketCount + 1;
                int failures, total = failures = 0;
                Span<Bucket> buckets = _buckets; // *not* a payload copy; this is in-place over the data
                foreach (ref readonly Bucket b in buckets)
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

            public override void Reset()
            {
                Span<Bucket> buckets = _buckets; // in-place over the data, not a copy
                foreach (ref Bucket b in buckets)
                {
                    b.Reset();
                }
            }
        }

        private static Type[] CheckExceptions(Type[]? tracked, out ExceptionStrategy strategy)
        {
            if (tracked is null)
            {
                strategy = ExceptionStrategy.Default;
                return [];
            }
            if (tracked.Length is 0)
            {
                strategy = ExceptionStrategy.None;
                return [];
            }
            strategy = ExceptionStrategy.CustomOpen;

            static bool Contains(Type[] array, Type type)
            {
                for (int i = 0; i < array.Length; i++)
                {
                    if (array[i] == type) return true;
                }

                return false;
            }

            // if we have Exception anywhere: we'll track everything
            if (Contains(tracked, typeof(Exception)))
            {
                strategy = ExceptionStrategy.Any;
                return [];
            }

            if (tracked.Length is 2
                && Contains(tracked, typeof(RedisConnectionException))
                && Contains(tracked, typeof(RedisTimeoutException)))
            {
                strategy = ExceptionStrategy.Default;
                return [];
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

        public override bool IsConnectionFault(Exception? fault)
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
