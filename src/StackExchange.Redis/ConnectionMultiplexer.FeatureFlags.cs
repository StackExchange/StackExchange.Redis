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
        /// <b>Always on from the v4 alpha, and no longer a switch.</b> It began as an opt-in so one build could be run
        /// both ways; once the suite passed on it, it became the default, and then the off-switch was removed so
        /// nothing - environment, <see cref="SetFeatureFlag"/> or otherwise - can put a database back on the
        /// shipped <c>RedisDatabase</c>. The value remains in this enum only so an existing
        /// <c>SetFeatureFlag("NewDatabaseSurface", ...)</c> call still parses; it has no effect.
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
        /// <b>Always on from the v4 alpha, and no longer a switch</b>, for the reason given on
        /// <see cref="NewDatabaseSurface"/>. It began off and a long way from green (222 failures and a hang); it
        /// became the default once the suite passed and no library path still built a shipped bridge, and the
        /// off-switch went so the old core cannot be reached at all ahead of its deletion.
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

    /// <summary>Always: the new surface is the only one; kept as a name only until the old core's files are deleted.</summary>
    /// <remarks>
    /// <b>No longer a switch</b> - neither the environment nor <see cref="SetFeatureFlag"/> can turn it off, so
    /// nothing can put a multiplexer back on the old core. The property survives only so the branches still
    /// testing it compile until the deletion removes them; see design/v4-alpha-plan.md, gate 3.
    /// </remarks>
    internal static bool NewDatabaseSurface => true;

    /// <summary>Always: the new core carries every connection; see <see cref="NewDatabaseSurface"/>.</summary>
    internal static bool NewCoreEngine => true;

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
