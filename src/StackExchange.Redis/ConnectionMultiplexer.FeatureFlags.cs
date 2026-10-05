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
        /// <b>On by default from the v4 alpha</b>, together with <see cref="NewCoreEngine"/>. Turning it off -
        /// <c>SEREDIS_NEW_DATABASE_SURFACE=0</c>, or <c>SetFeatureFlag</c> - returns the shipped
        /// <c>RedisDatabase</c>, which remains only as a way back while the alpha is proven, and is scheduled
        /// for deletion with the rest of the old core.
        /// </para>
        /// <para>
        /// Set it before taking a database: instances are cached per multiplexer, so a connection that has
        /// already handed out database 0 will keep handing out the same one.
        /// </para>
        /// </remarks>
        NewDatabaseSurface = 4,

        /// <summary>
        /// Run <see cref="NewDatabaseSurface"/> over the new core's own connections, rather than over the
        /// shipped pipeline.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Two flags because these are two independent questions.</b> The surface flag decides how a
        /// command is rendered and its reply read; this one decides what carries it. Until now the answer
        /// was always the shipped pipeline, reached through a <c>Message</c> shim - which cannot write a
        /// batch as one contiguous run, so every batch fell back to <c>RedisBatch : RedisDatabase</c> and
        /// kept the whole of the old surface alive. That fallback is the last thing holding
        /// <c>RedisDatabase</c> up, and only a real connection removes it.
        /// </para>
        /// <para>
        /// <b>On by default from the v4 alpha.</b> It began off and a long way from green (222 failures and
        /// a hang); it was made the default once the full suite passed on it and no library path still
        /// built a shipped bridge. <c>SEREDIS_NEW_CORE_ENGINE=0</c>, or <c>SetFeatureFlag</c>, returns to the
        /// shipped core - kept only as a way back while the alpha is proven; see design/v4-alpha-plan.md.
        /// </para>
        /// </remarks>
        NewCoreEngine = 8,
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

        // The new core is the DEFAULT from the v4 alpha; the environment can still turn either half off, so
        // the shipped core stays one variable away while the alpha is proven - and so the suite can still be
        // run both ways from one build. Guarded because reading the environment is not permitted in every
        // host, and a host that cannot read it gets the default.
        SetFeatureFlag(nameof(FeatureFlags.NewDatabaseSurface), true);
        SetFeatureFlag(nameof(FeatureFlags.NewCoreEngine), true);
        try
        {
            if (IsOff(Environment.GetEnvironmentVariable("SEREDIS_NEW_DATABASE_SURFACE")))
            {
                SetFeatureFlag(nameof(FeatureFlags.NewDatabaseSurface), false);
            }

            if (IsOff(Environment.GetEnvironmentVariable("SEREDIS_NEW_CORE_ENGINE")))
            {
                SetFeatureFlag(nameof(FeatureFlags.NewCoreEngine), false);
            }
        }
        catch { }

        static bool IsOff(string? value) => value is "0" or "false" or "FALSE" or "False";
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

    /// <inheritdoc cref="FeatureFlags.NewCoreEngine"/>
    internal static bool NewCoreEngine => (s_featureFlags & FeatureFlags.NewCoreEngine) != 0;

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
