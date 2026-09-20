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
        /// Return databases built on the new RESP context surface, rather than <c>RedisDatabase</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>The migration switch for the core replacement.</b> The transitional database implements the
        /// whole of <see cref="IDatabase"/> over the new rendering and parsing, falling back to the shipped
        /// database for anything that has not moved - so this changes how commands are written and replies
        /// are read, and nothing about connections, the bridge or the pipeline.
        /// </para>
        /// <para>
        /// <b>Off by default, and deliberately a flag rather than a branch.</b> The remaining failures are
        /// a known, shrinking list, and a flag lets the suite be run both ways from one build: the default
        /// stays green while the new surface is worked down, instead of the work living on a red tree or a
        /// long-lived branch that drifts.
        /// </para>
        /// <para>
        /// Set it before taking a database: instances are cached per multiplexer, so a connection that has
        /// already handed out database 0 will keep handing out the same one.
        /// </para>
        /// </remarks>
        NewDatabaseSurface = 4,
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

        // The migration switch is settable from the environment so one build can be run both ways - the
        // default suite green, and a second pass on the new surface - rather than the work living on a red
        // tree. Guarded because reading the environment is not permitted in every host.
        try
        {
            if (Environment.GetEnvironmentVariable("SEREDIS_NEW_DATABASE_SURFACE") is "1" or "true" or "TRUE")
            {
                SetFeatureFlag(nameof(FeatureFlags.NewDatabaseSurface), true);
            }
        }
        catch { }
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

    /// <inheritdoc cref="FeatureFlags.NewDatabaseSurface"/>
    internal static bool NewDatabaseSurface => (s_featureFlags & FeatureFlags.NewDatabaseSurface) != 0;

    /// <summary>
    /// Whether the connection of this type to this endpoint is read by a thread we own; <c>null</c> if there
    /// is no such connection.
    /// </summary>
    /// <remarks>
    /// For tests and diagnostics: the <see cref="FeatureFlags.DedicatedThreads"/> flag is a request, and this
    /// is what actually happened. Note that under RESP3 there is no separate subscription connection, so
    /// asking about <see cref="ConnectionType.Subscription"/> answers about the shared one.
    /// </remarks>
    bool? IInternalConnectionMultiplexer.IsSyncReader(EndPoint endpoint, ConnectionType connectionType)
        => GetPhysical(endpoint, connectionType)?.IsSyncReader;

    /// <summary>As <c>IsSyncReader</c>, for the writer.</summary>
    bool? IInternalConnectionMultiplexer.IsSyncWriter(EndPoint endpoint, ConnectionType connectionType)
        => GetPhysical(endpoint, connectionType)?.IsSyncWriter;

    private PhysicalConnection? GetPhysical(EndPoint endpoint, ConnectionType connectionType)
        => TryResolveServerEndPoint(endpoint)?.GetBridge(connectionType, create: false)?.Physical;
}
