using StackExchange.Redis.Interfaces;

namespace StackExchange.Redis.Availability;

/// <summary>
/// The group's database: the ordinary <see cref="RedisDatabase"/> funnel over a context whose executor
/// (<see cref="RespGroupExecutor"/>) resolves the currently-active member per send.
/// </summary>
/// <remarks>
/// <para>
/// <b>This used to be 628 generated capture-and-replay members.</b> Every one of them existed to capture a
/// command's arguments, resolve the active member, and replay the call against it - because the decoration
/// happened above the command surface. With the decoration an executor below it, those members have
/// nothing left to do, and what remains here is only what the base funnel cannot know: the group's feature
/// flags and diagnostic name, and the cursor scans, which must pin to one member (see
/// <c>MultiGroupDatabase.Scans.cs</c>).
/// </para>
/// <para>
/// Note what is <i>not</i> here: nothing about connecting, reconnecting, backlogs, slots or routing.
/// The active member is an entire multiplexer with its own executor chain, and the group executor hands
/// the command to it. That is the whole of a group's job, and the reason this is short.
/// </para>
/// <para>
/// <b>The command map comes from configuration, not connection state</b> - each member is configured
/// separately, and the group takes the map from its members' configuration - so the context answers while
/// every member is down. The database index is configuration too: a defaulted index resolves to the
/// members' shared <c>DefaultDatabase</c>, which <c>ValidateConsistentConfiguration</c> demands they agree
/// on - so the context is fully determined, buildable and cacheable with nothing reachable at all.
/// </para>
/// <para>
/// <b>The cache is the active member's, resolved per command.</b> Not a shared group cache: a cached
/// reply is only sound while the connection that produced it is the one being asked and its
/// <c>CLIENT TRACKING</c> registration is still live, so serving member A's values while B is active
/// would be wrong - and silently so. Per-member caches make a switch a no-op for correctness, and the
/// existing "flush when tracking drops" rule already covers the case that actually matters, since a
/// failover usually happens BECAUSE the old member's connection died. A switch away from a healthy
/// member leaves its cache warm for switching back, which nuking a shared cache would throw away.
/// </para>
/// <para>
/// <b>Knowing the replication topology would not change this.</b> The rule is not "the data might
/// differ" - that is incidental - but that <i>a cache entry carries an implicit subscription, and
/// subscriptions do not transfer</i>. <c>CLIENT TRACKING</c> is registered per connection, so
/// inheriting A's entries while talking to B inherits no registration on B: those entries become
/// unfalsifiable rather than merely possibly-stale, even if B's data is byte-identical. Active-active
/// and geo-replication change the invalidation <i>latency</i> (bounded by replication lag rather than
/// a round trip), which bounds a safe maximum age - not what happens on a switch.
/// </para>
/// <para>
/// <b>Our own writes are the exception, and travel to every member's cache</b>
/// (<see cref="Caching.RespClientCache.SetPeers"/>): a write made while B is active reaches A's server only by
/// replication, so without that, failing back could hand a caller the value they replaced. An inactive
/// member's tracking connection is kept honest the same way an active one is: group members always run
/// heartbeat consistency checks, so every endpoint is pinged each heartbeat, a half-open connection trips
/// the dead-socket rule, and the failure flushes that member's cache.
/// </para>
/// </remarks>
internal sealed partial class MultiGroupDatabase : RedisDatabase
{
    private readonly MultiGroupMultiplexer _parent;
    private readonly int _database;
    private readonly object? _asyncState;

    /// <summary>Create the group's database over a group-executor context.</summary>
    /// <param name="parent">The group this database belongs to.</param>
    /// <param name="database">The database index; already resolved, never negative (see
    /// <c>MultiGroupMultiplexer.GetDatabase</c>).</param>
    /// <param name="asyncState">The async-state stamped onto the tasks this database hands out.</param>
    internal MultiGroupDatabase(MultiGroupMultiplexer parent, int database, object? asyncState)
        : base(BuildContext(parent, database), parent, asyncState)
    {
        _parent = parent;
        _database = database;
        _asyncState = asyncState;
    }

    /// <summary>The context over whichever member is currently active.</summary>
    private static RespDatabaseContext BuildContext(MultiGroupMultiplexer parent, int database) => new(
        new RespContext(
                parent.CommandMap,
                database: database,
                serverType: ServerType.Standalone)
            .WithExecutor(new RespGroupExecutor(
                () => ActiveExecutor(parent, database),
                database,
                () => DescribeUnavailable(parent),
                parent.GetNextFailover)
            { Group = parent })
            .WithCacheResolver(() => parent.TryGetActive()?.ClientCache));

    private static RespExecutorBase? ActiveExecutor(MultiGroupMultiplexer parent, int database)
        => parent.TryGetActive()?.GetDatabase(database) is IRespTarget target ? target.Context.Executor : null;

    private static string DescribeUnavailable(MultiGroupMultiplexer parent)
    {
        // the group's own wording rather than a second copy of it: this asks the same question the facade
        // asks and reports the answer, so the two cannot drift apart
        try
        {
            _ = parent.Active;
            return "No connection group member is available.";
        }
        catch (RedisConnectionException ex)
        {
            return ex.Message;
        }
    }

    /// <inheritdoc/>
    /// <remarks>Folds in the currently-active member's features (when there is one) and labels with its name.</remarks>
    private protected override DatabaseFeatureFlags GetFeaturesCore(out string name)
    {
        var active = TryGetActiveDatabase();
        var features = active is null ? DatabaseFeatureFlags.None : active.GetFeatures(out _);
        name = ((IConnectionGroup)_parent).ActiveMember?.Name ?? "";
        return features | DatabaseFeatureFlags.ConnectionGroup | DatabaseFeatureFlags.Failover;
    }

    // uses the active member's name (via GetFeaturesCore) when a member is active
    public override string ToString() => this.BuildString();

    // for high DB numbers this might allocate even for null async-state scenarios; unavoidable for now
    private IDatabase GetActiveDatabase() => _parent.Active.GetDatabase(_database, _asyncState);

    // non-throwing twin of GetActiveDatabase: returns null when the group currently has no active
    // member, for use by members that have an obvious trivial result when disconnected
    private IDatabase? TryGetActiveDatabase() => _parent.TryGetActive()?.GetDatabase(_database, _asyncState);
}
