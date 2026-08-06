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
[Experimental(Experiments.GeoRedundantFailover, UrlFormat = Experiments.UrlFormat)]
public abstract class CircuitBreaker
{
    internal const double DefaultFailureRateThreshold = 10;
    internal const int DefaultMinimumNumberOfFailures = 1000;
    internal static readonly TimeSpan DefaultMetricsWindowSize = TimeSpan.FromSeconds(2);

    private static readonly CircuitBreaker DefaultInstance = new DefaultCircuitBreaker(
#pragma warning disable SA1114 // Parameter list should follow declaration - false positive: the #if directive splits the argument list
#if NET8_0_OR_GREATER
        null,
#endif
#pragma warning restore SA1114
        DefaultFailureRateThreshold,
        DefaultMinimumNumberOfFailures,
        DefaultMetricsWindowSize);

    /// <summary>
    /// Default circuit-breaker logic: trips when the failure rate over a short rolling window crosses a threshold.
    /// </summary>
    public static CircuitBreaker Default => DefaultInstance;

    /// <summary>
    /// No circuit-breaker logic is applied.
    /// </summary>
    public static CircuitBreaker None => NulCircuitBreaker.Instance;

    /// <summary>
    /// Allows configuration of the default <see cref="CircuitBreaker"/> implementation.
    /// </summary>
    [Experimental(Experiments.GeoRedundantFailover, UrlFormat = Experiments.UrlFormat)]
    public sealed class Builder
    {
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
            if (FailureRateThreshold is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(FailureRateThreshold), FailureRateThreshold, "A percentage between 0 and 100 is required.");
            if (MinimumNumberOfFailures < 1) throw new ArgumentOutOfRangeException(nameof(MinimumNumberOfFailures), MinimumNumberOfFailures, "At least one failure is required; use CircuitBreaker.None to disable circuit-breaking.");
            if (MetricsWindowSize <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(MetricsWindowSize), MetricsWindowSize, "A positive window is required.");
            if (MetricsWindowSize.TotalSeconds > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(MetricsWindowSize), MetricsWindowSize, "The window is too large.");

            if ((FailureRateThreshold is DefaultFailureRateThreshold
#if NET8_0_OR_GREATER
                 & TimeProvider is null
#endif
                 & MinimumNumberOfFailures is DefaultMinimumNumberOfFailures)
                && MetricsWindowSize == DefaultMetricsWindowSize)
                return DefaultInstance;

            return new DefaultCircuitBreaker(
#pragma warning disable SA1114 // Parameter list should follow declaration - false positive: the #if directive splits the argument list
#if NET8_0_OR_GREATER
                TimeProvider,
#endif
#pragma warning restore SA1114
                FailureRateThreshold,
                MinimumNumberOfFailures,
                MetricsWindowSize);
        }

        /// <summary>
        /// Create a new circuit-breaker instance.
        /// </summary>
        public static implicit operator CircuitBreaker(Builder builder) => builder.Create();
    }

    /// <summary>
    /// Create an object to collate observations for a connection.
    /// </summary>
    public abstract Accumulator CreateAccumulator();

    internal static bool DefaultIsFailure(in FaultContext fault)
    {
        if (fault.ConnectionFailureType is not ConnectionFailureType.None) return true;
        switch (fault.ErrorKind)
        {
            // what things *don't* trip the breaker?
            case RedisErrorKind.None: // not even flagged
            case RedisErrorKind.UnknownCommand: // application failure
            case RedisErrorKind.ExecAbort: // transient to one command
            case RedisErrorKind.WrongType: // application failure
            case RedisErrorKind.NoPermission: // using the wrong keys?
            case RedisErrorKind.UnknownError: // not sure what it is, but it starts ERR
            case RedisErrorKind.Unknown: // pretty much anything we don't recognize; should we assume this is BAD?
                return false;
            default:
                return true;
        }
    }

    /// <summary>
    /// Collates observations for a connection.
    /// </summary>
    public abstract class Accumulator()
    {
        /// <summary>
        /// Record a message outcome.
        /// </summary>
        /// struct arg here is in case we want to add more things later
        public abstract void ObserveResult(in FaultContext fault);

        /// <summary>
        /// Indicate whether a given fault should be considered a failure for the <see cref="Accumulator"/>.
        /// </summary>
        protected virtual bool IsFailure(in FaultContext fault) => DefaultIsFailure(in fault);

        /// <summary>
        /// Indicate whether the connection is currently considered healthy, without recording an observation.
        /// </summary>
        /// <returns>True if the connection is considered healthy.</returns>
        public abstract bool IsHealthy();

        /// <summary>
        /// Discard all accumulated observations, returning to a clean state.
        /// </summary>
        public abstract void Reset();

        internal bool Trip(Exception? fault)
        {
            if (fault is not null)
            {
                var ctx = new FaultContext(fault);
                if (IsFailure(ctx))
                {
                    ObserveResult(ctx);
                    return !IsHealthy();
                }
                // otherwise, treat as success for the purposes of counting
            }

            ObserveResult(in FaultContext.Success);
            return false; // never trip through success
        }
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
            public override void ObserveResult(in FaultContext context) { }
            public override bool IsHealthy() => true;
            public override void Reset() { }
        }

        // note we leave IsConnectionFault alone - that would impact RetryDatabase, where this is the key
    }
    private sealed class DefaultCircuitBreaker : CircuitBreaker
    {
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
#if NET8_0_OR_GREATER
            TimeProvider? timeProvider,
#endif
            double failureRateThreshold,
            int minimumNumberOfFailures,
            TimeSpan metricsWindowSize)
        {
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

            public override void ObserveResult(in FaultContext result)
            {
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
                bucket.Count(epoch, success: !result.IsFault);
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
    }
}
