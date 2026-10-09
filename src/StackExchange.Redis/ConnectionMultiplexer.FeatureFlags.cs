using System;
using System.ComponentModel;
using System.Net;
using System.Threading;

namespace StackExchange.Redis;

public partial class ConnectionMultiplexer
{
    private static FeatureFlags s_featureFlags;

    [Flags]
    private enum FeatureFlags
    {
        None,
        PreventThreadTheft = 1,

        /// <summary>
        /// Service connections from threads this library owns, rather than from the global thread-pool.
        /// </summary>
        /// <remarks>
        /// For an application whose thread-pool is saturated - most often by sync-over-async somewhere, though
        /// the cause does not matter here - the reply from redis cannot be processed, because processing it
        /// needs a thread and every thread is waiting on one. Owning the reader and writer takes this library
        /// out of that queue. It does not *fix* the thread-pool, and nothing here can: it means only that redis
        /// traffic keeps flowing while the real problem is found. See docs/SyncOverAsync.md.
        /// <para>
        /// Costs a reader and a writer thread per connection, so it is worth thinking about before enabling it
        /// against a very wide cluster, where connection counts scale with the number of shards.
        /// </para>
        /// </remarks>
        DedicatedThreads = 2,

        /// <summary>
        /// Read and parse on one loop, rather than filling from the socket on one task and parsing on another.
        /// </summary>
        /// <remarks>
        /// The split is the default from 4.0 (the intent of PR #3251): with one loop, nothing reads while a parse
        /// pass runs, so the socket idles and throughput on concurrent reads with a payload is capped. It costs a
        /// few percent where there is almost nothing to parse; this restores the single loop, for comparison or
        /// for a workload that measures better without the split. Connections made after it is set use it.
        /// </remarks>
        SingleReadLoop = 4,

        /// <summary>
        /// Experimental: a send that finds the connection's write lock busy leaves its command for the lock
        /// holder to write, rather than waiting for the lock.
        /// </summary>
        /// <remarks>
        /// With many more concurrent callers than cores, a write lock taken once per command convoys: measured
        /// at 50 callers on 12 cores, 59% of thread time was spent in <c>Monitor.Enter</c> and throughput was
        /// half of 3.x, which never waits there (it queues to a backlog the holder writes). Combining does the
        /// same inside the connection; an uncontended send is unchanged. Connections made after it is set use it.
        /// </remarks>
        CombineWrites = 8,

        /// <summary>
        /// On by default: a caller whose request is alone in flight on its connection sends its own bytes, rather than
        /// waking the writer loop. <c>SEREDIS_INLINESENDS=0</c>, or clearing it in code, restores the writer for every send.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Every hand-off to the thread pool wakes a worker that then spins idle for a while, and for a sequential
        /// caller that spinning was most of the CPU per request (measured: 118 -> 30us per INCR with the pool's spin
        /// disabled). Sending inline removes the writer's hand-off. Bounded: the caller sends only what was staged when
        /// it claimed the writer, and leaves anything else - and a send that would block - to the writer loop. Only when
        /// nothing else is in flight: sending inline whenever the writer was idle cost <c>incr-conc64</c> 4%.
        /// </para>
        /// <para>
        /// RespFest, CPU per op off -> on: <c>work-100-seq</c> 265.8 -> 203.0us (v3 207.3), <c>incr-seq</c> 69.2 -> 66.5,
        /// <c>get-1k-seq</c> 70.3 -> 67.7 (+2% ops/s); concurrent within noise. Made the default after ten clean
        /// full-suite runs with it on. Connections made after it is changed use the new setting.
        /// </para>
        /// </remarks>
        InlineSends = 16,
    }

    private static void SetAutodetectFeatureFlags()
    {
        bool value = false;
        try
        {
            // attempt to detect a known problem scenario
            value = SynchronizationContext.Current?.GetType()?.Name
                == "LegacyAspNetSynchronizationContext";
        }
        catch { }
        SetFeatureFlag(nameof(FeatureFlags.PreventThreadTheft), value);
        SetFeatureFlag(nameof(FeatureFlags.InlineSends), true); // on by default; the environment can still clear it
        ApplyEnvironmentFeatureFlags();
    }

    /// <summary>
    /// Each flag can also be set from the environment: flag <c>Foo</c> reads <c>SEREDIS_FOO</c>.
    /// </summary>
    /// <remarks>
    /// <c>1</c>/<c>true</c>/<c>yes</c> sets it and <c>0</c>/<c>false</c>/<c>no</c> clears it; anything else, or
    /// nothing, leaves it alone. Applied after autodetection, so the environment overrides a guess; a later
    /// <see cref="SetFeatureFlag"/> call in code overrides both. For support and benchmarking, where changing
    /// the environment is easier than changing the code - a flag set this way is the same flag as one set in code.
    /// </remarks>
    private static void ApplyEnvironmentFeatureFlags() => ApplyEnvironmentFeatureFlags(Environment.GetEnvironmentVariable);

    /// <inheritdoc cref="ApplyEnvironmentFeatureFlags()"/>
    /// <param name="read">Reads one variable; the environment, except under test.</param>
    internal static void ApplyEnvironmentFeatureFlags(Func<string, string?> read)
    {
        foreach (var name in Enum.GetNames(typeof(FeatureFlags)))
        {
            if (name == nameof(FeatureFlags.None)) continue;

            string? raw;
            try
            {
                raw = read("SEREDIS_" + name.ToUpperInvariant());
            }
            catch
            {
                return; // a host that forbids reading the environment gets the defaults
            }

            switch (raw?.Trim().ToLowerInvariant())
            {
                case "1" or "true" or "yes":
                    SetFeatureFlag(name, true);
                    break;
                case "0" or "false" or "no":
                    SetFeatureFlag(name, false);
                    break;
            }
        }
    }

    /// <summary>
    /// Enables or disables a feature flag.
    /// This should only be used under support guidance, and should not be rapidly toggled.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Browsable(false)]
    public static void SetFeatureFlag(string flag, bool enabled)
    {
        if (Enum.TryParse<FeatureFlags>(flag, true, out var flags))
        {
            if (enabled) s_featureFlags |= flags;
            else s_featureFlags &= ~flags;
        }
    }

    /// <summary>
    /// Returns the state of a feature flag.
    /// This should only be used under support guidance.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Browsable(false)]
    public static bool GetFeatureFlag(string flag)
        => Enum.TryParse<FeatureFlags>(flag, true, out var flags)
        && (s_featureFlags & flags) == flags;

    internal static bool PreventThreadTheft => (s_featureFlags & FeatureFlags.PreventThreadTheft) != 0;

    internal static bool DedicatedThreads => (s_featureFlags & FeatureFlags.DedicatedThreads) != 0;

    internal static bool SingleReadLoop => (s_featureFlags & FeatureFlags.SingleReadLoop) != 0;

    internal static bool CombineWrites => (s_featureFlags & FeatureFlags.CombineWrites) != 0;

    internal static bool InlineSends => (s_featureFlags & FeatureFlags.InlineSends) != 0;

    /// <summary>
    /// Whether the connection of this type to this endpoint is read by a thread we own; <c>null</c> if there
    /// is no such connection.
    /// </summary>
    /// <remarks>
    /// For tests and diagnostics: the <see cref="FeatureFlags.DedicatedThreads"/> flag is a request, and this
    /// is what actually happened. Note that under RESP3 with <see cref="ConfigurationOptions.SharedSubscriptionConnection"/>
    /// there is no separate subscription connection, so asking about <see cref="ConnectionType.Subscription"/> answers
    /// about the shared one.
    /// </remarks>
    bool? IInternalConnectionMultiplexer.IsSyncReader(EndPoint endpoint, ConnectionType connectionType)
        => ConnectionsIfCreated?.IsDedicatedThread(endpoint, connectionType, writer: false);

    /// <summary>As <c>IsSyncReader</c>, for the writer.</summary>
    bool? IInternalConnectionMultiplexer.IsSyncWriter(EndPoint endpoint, ConnectionType connectionType)
        => ConnectionsIfCreated?.IsDedicatedThread(endpoint, connectionType, writer: true);
}
