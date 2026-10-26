using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace StackExchange.Redis;

public partial class ConnectionMultiplexer
{
    // volatile: written by SwitchPrimary on worker/handler threads and read by the heartbeat
    // (IsSentinelPrimaryDemoted / RecoverDemotedPrimary) - the demotion check must see the latest switch
    internal volatile EndPoint? currentSentinelPrimaryEndPoint;
    internal Timer? sentinelPrimaryReconnectTimer;
    internal readonly Dictionary<string, ConnectionMultiplexer> sentinelConnectionChildren = new();
    internal ConnectionMultiplexer? sentinelConnection;
    private int _sentinelInitialized;
    private int _demotedPrimaryRecoveryActive;
    private int _sentinelSwitchActive;
    private int _sentinelAddressUpdateActive;
    private readonly HashSet<string> _sentinelAddressUpdatesPending = new(StringComparer.Ordinal);
    private int _sentinelSwitchPending;
    private HashSet<EndPoint> _sentinelDownRoutes = new();
    internal bool IsSentinelManaged { get; private set; }

    /// <summary>
    /// Initializes the connection as a Sentinel connection and adds the necessary event handlers to track changes to the managed primaries.
    /// </summary>
    /// <remarks>
    /// Runs once. It is reached from the connect paths, and also from the reconfigure loop when a connection
    /// that soft-failed its initial connect (<see cref="ConfigurationOptions.AbortOnConnectFail"/> is <c>false</c>) later
    /// reaches a sentinel: without that, a connection that started while the sentinels were down would never subscribe
    /// to <c>+switch-master</c>, and would track failovers only through the fallback polling.
    /// </remarks>
    /// <param name="log">The <see cref="ILogger"/> to log to, if any.</param>
    internal void InitializeSentinel(ILogger? log)
    {
        if (!_isSentinel || Interlocked.Exchange(ref _sentinelInitialized, 1) != 0)
        {
            return;
        }

        // Subscribe to sentinel change events
        ISubscriber sub = GetSubscriber();

        if (sub.SubscribedEndpoint(RedisChannel.Literal("+switch-master")) == null)
        {
            sub.Subscribe(
                RedisChannel.Literal("+switch-master"),
                (__, message) =>
                {
                    string[] messageParts = ((string)message!).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    // We don't care about the result of this - we're just trying
                    _ = Format.TryParseEndPoint(string.Format("{0}:{1}", messageParts[1], messageParts[2]), out var switchBlame);

                    // Resolve the child under the lock, then start discovery/reconfiguration outside it:
                    // services sharing a sentinel connection must not hold up one another's failover.
                    ConnectionMultiplexer? switchTarget = null;
                    lock (sentinelConnectionChildren)
                    {
                        // Switch the primary if we have connections for that service
                        if (sentinelConnectionChildren.TryGetValue(messageParts[0], out ConnectionMultiplexer? child))
                        {
                            // Is the connection still valid?
                            if (child.IsDisposed)
                            {
                                ForgetDisposedChild(messageParts[0], child);
                            }
                            else
                            {
                                switchTarget = child;
                            }
                        }
                    }

                    if (switchTarget is not null)
                    {
                        SwitchPrimaryAsync(switchBlame, switchTarget).RedisFireAndForget();
                    }
                },
                CommandFlags.FireAndForget);
        }

        // If we lose connection to a sentinel server,
        // we need to reconfigure to make sure we still have a subscription to the +switch-master channel
        ConnectionFailed += (sender, e) =>
            // Reconfigure to get subscriptions back online
            ReconfigureAsync(first: false, reconfigureAll: true, log, e.EndPoint, "Lost sentinel connection", false).RedisFireAndForget();

        // Subscribe to new sentinels being added
        if (sub.SubscribedEndpoint(RedisChannel.Literal("+sentinel")) == null)
        {
            sub.Subscribe(
                RedisChannel.Literal("+sentinel"),
                (_, message) =>
                {
                    string[] messageParts = ((string)message!).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    // "<type> <name> <ip> <port> @ <master-name> <master-ip> <master-port>": the service follows the "@"
                    int separator = Array.IndexOf(messageParts, "@");
                    if (separator >= 0 && separator + 1 < messageParts.Length)
                        UpdateSentinelAddressListAsync(messageParts[separator + 1]).RedisFireAndForget();
                },
                CommandFlags.FireAndForget);
        }
    }

    /// <summary>
    /// Create a new <see cref="ConnectionMultiplexer"/> instance that connects to a Sentinel server.
    /// </summary>
    /// <param name="configuration">The string configuration to use for this multiplexer.</param>
    /// <param name="log">The <see cref="TextWriter"/> to log to.</param>
    public static ConnectionMultiplexer SentinelConnect(string configuration, TextWriter? log = null) =>
        SentinelConnect(ConfigurationOptions.Parse(configuration), log);

    /// <summary>
    /// Create a new <see cref="ConnectionMultiplexer"/> instance that connects to a Sentinel server.
    /// </summary>
    /// <param name="configuration">The string configuration to use for this multiplexer.</param>
    /// <param name="log">The <see cref="TextWriter"/> to log to.</param>
    public static Task<ConnectionMultiplexer> SentinelConnectAsync(string configuration, TextWriter? log = null) =>
        SentinelConnectAsync(ConfigurationOptions.Parse(configuration), log);

    /// <summary>
    /// Create a new <see cref="ConnectionMultiplexer"/> instance that connects to a Sentinel server.
    /// </summary>
    /// <param name="configuration">The configuration options to use for this multiplexer.</param>
    /// <param name="log">The <see cref="TextWriter"/> to log to.</param>
    public static ConnectionMultiplexer SentinelConnect(ConfigurationOptions configuration, TextWriter? log = null)
    {
        Dependencies.Assert();
        Validate(configuration);

        return ConnectImpl(configuration, log, ServerType.Sentinel);
    }

    /// <summary>
    /// Create a new <see cref="ConnectionMultiplexer"/> instance that connects to a Sentinel server.
    /// </summary>
    /// <param name="configuration">The configuration options to use for this multiplexer.</param>
    /// <param name="log">The <see cref="TextWriter"/> to log to.</param>
    public static Task<ConnectionMultiplexer> SentinelConnectAsync(ConfigurationOptions configuration, TextWriter? log = null)
    {
        Dependencies.Assert();
        Validate(configuration);

        return ConnectImplAsync(configuration, log, ServerType.Sentinel);
    }

    /// <summary>
    /// Create a new <see cref="ConnectionMultiplexer"/> instance that connects to a sentinel server, discovers the current primary server
    /// for the specified <see cref="ConfigurationOptions.ServiceName"/> in the config and returns a managed connection to the current primary server.
    /// </summary>
    /// <param name="configuration">The configuration options to use for this multiplexer.</param>
    /// <param name="log">The <see cref="TextWriter"/> to log to.</param>
    private static ConnectionMultiplexer SentinelPrimaryConnect(ConfigurationOptions configuration, TextWriter? log = null)
    {
        // Use separate sentinel credentials when provided on the configuration
        var sentinelConfig = configuration.Clone();
        sentinelConfig.User = configuration.SentinelUser;
        sentinelConfig.Password = configuration.SentinelPassword;

        var sentinelConnection = SentinelConnect(sentinelConfig, log);
        try
        {
            var muxer = sentinelConnection.GetSentinelMasterConnection(configuration, log);
            // Set reference to sentinel connection so that we can dispose it
            muxer.sentinelConnection = sentinelConnection;

            return muxer;
        }
        catch
        {
            try { sentinelConnection.Dispose(); } catch { }
            throw;
        }
    }

    /// <summary>
    /// Create a new <see cref="ConnectionMultiplexer"/> instance that connects to a sentinel server, discovers the current primary server
    /// for the specified <see cref="ConfigurationOptions.ServiceName"/> in the config and returns a managed connection to the current primary server.
    /// </summary>
    /// <param name="configuration">The configuration options to use for this multiplexer.</param>
    /// <param name="writer">The <see cref="TextWriter"/> to log to.</param>
    private static async Task<ConnectionMultiplexer> SentinelPrimaryConnectAsync(ConfigurationOptions configuration, TextWriter? writer = null)
    {
        // Use separate sentinel credentials when provided on the configuration
        var sentinelConfig = configuration.Clone();
        sentinelConfig.User = configuration.SentinelUser;
        sentinelConfig.Password = configuration.SentinelPassword;

        var sentinelConnection = await SentinelConnectAsync(sentinelConfig, writer).ForAwait();
        try
        {
            var muxer = await sentinelConnection.GetSentinelMasterConnectionAsync(configuration, writer).ForAwait();
            // Set reference to sentinel connection so that we can dispose it
            muxer.sentinelConnection = sentinelConnection;

            return muxer;
        }
        catch
        {
            try { await sentinelConnection.DisposeAsync().ForAwait(); } catch { }
            throw;
        }
    }

    /// <summary>
    /// Returns a managed connection to the primary server indicated by the <see cref="ConfigurationOptions.ServiceName"/> in the config.
    /// </summary>
    /// <param name="config">The configuration to be used when connecting to the primary.</param>
    /// <param name="log">The writer to log to, if any.</param>
    public ConnectionMultiplexer GetSentinelMasterConnection(ConfigurationOptions config, TextWriter? log = null)
        // The public synchronous connect boundary only. Background recovery and ConnectAsync call the async core.
        => GetSentinelMasterConnectionAsync(config, log, synchronousConnect: true).GetAwaiter().GetResult();

    private async Task<ConnectionMultiplexer> GetSentinelMasterConnectionAsync(ConfigurationOptions config, TextWriter? log, bool synchronousConnect = false)
    {
        // A soft-failed SentinelConnect cannot detect a server type, but _isSentinel still records its intent.
        if (ServerSelectionStrategy.ServerType != ServerType.Sentinel && (config.AbortOnConnectFail || !_isSentinel))
        {
            throw new RedisConnectionException(
                ConnectionFailureType.UnableToConnect,
                CommandFlags.None,
                "Sentinel: The ConnectionMultiplexer is not a Sentinel connection. Detected as: " + ServerSelectionStrategy.ServerType);
        }

        var serviceName = config.ServiceName;
        if (serviceName.IsNullOrEmpty())
        {
            throw new ArgumentException("A ServiceName must be specified.");
        }

        lock (sentinelConnectionChildren)
        {
            if (sentinelConnectionChildren.TryGetValue(serviceName, out var sentinelConnectionChild) && !sentinelConnectionChild.IsDisposed)
                return sentinelConnectionChild;
        }

        bool success = false;
        ConnectionMultiplexer? connection = null;
        EndPointCollection? endpoints = null;
        RedisConnectionException? failure = null;

        try
        {
            var sw = ValueStopwatch.StartNew();
            do
            {
                // Sentinel has some fun race behavior internally - give things a few shots for a quicker overall connect.
                const int queryAttempts = 2;

                SentinelPrimary? primary = null;
                for (int i = 0; i < queryAttempts && primary is null; i++)
                {
                    primary = await GetConfiguredPrimaryForServiceAsync(serviceName).ForAwait();
                }

                if (primary is null)
                {
                    failure = new RedisConnectionException(
                        ConnectionFailureType.UnableToConnect,
                        CommandFlags.None,
                        $"Sentinel: Failed connecting to configured primary for service: {config.ServiceName}");
                    if (config.AbortOnConnectFail)
                    {
                        throw failure;
                    }

                    if (connection is not null) try { await connection.DisposeAsync().ForAwait(); } catch { }
                    connection = await SeedConnectionAsync(failure).ForAwait();
                    break;
                }

                var newPrimaryEndPoint = primary.EndPoint;
                SentinelReplica[]? replicaEndPoints = null;
                for (int i = 0; i < queryAttempts && replicaEndPoints is null; i++)
                {
                    replicaEndPoints = await GetReplicasForServiceAsync(serviceName, primary).ForAwait();
                }

                endpoints = config.EndPoints.Clone();

                // Replace the primary endpoint, if we found another one
                // If not, assume the last state is the best we have and minimize the race
                if (endpoints.Count == 1)
                {
                    endpoints[0] = newPrimaryEndPoint;
                }
                else
                {
                    endpoints.Clear();
                    endpoints.TryAdd(newPrimaryEndPoint);
                }

                if (replicaEndPoints is not null)
                {
                    // a replica sentinel reports down is not connected to: it is added once its flags clear
                    foreach (var replica in replicaEndPoints)
                    {
                        if (!replica.Down) endpoints.TryAdd(replica.EndPoint);
                    }
                }

                if (connection is not null) try { await connection.DisposeAsync().ForAwait(); } catch { }
                connection = await ConnectImplAsync(config, log, endpoints: endpoints, synchronousConnect: synchronousConnect).ForAwait();

                // Preserve the synchronous Connect soft timeout: a disconnected multiplexer is returned while its
                // initial reconfigure continues; ROLE and the initial switch are recovery work in this case.
                if (synchronousConnect && !config.AbortOnConnectFail && !connection.IsConnected)
                {
                    failure = connection.LastException as RedisConnectionException;
                    break;
                }

                // verify role is primary according to:
                // https://redis.io/docs/latest/develop/reference/sentinel-clients/ Step 3: ROLE (or INFO) on the node
                // sentinel named; a non-primary answer means a stale sentinel view, so the loop asks again.
                bool isPrimary;
                var server = connection.GetServer(newPrimaryEndPoint);
                // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
                if (server is { })
                {
                    try
                    {
                        isPrimary = connection.CommandMap.IsAvailable(RedisCommand.ROLE)
                            ? (await server.RoleAsync().ForAwait())?.Value == Role.LabelForMaster
                            : !server.IsReplica;
                    }
                    catch
                    {
                        // fallback if ROLE unavailable but not declared; see #3064
                        isPrimary = !server.IsReplica;
                    }

                    if (isPrimary)
                    {
                        success = true;
                        break;
                    }
                }

                await Task.Delay(100).ForAwait();
            }
            while (sw.ElapsedMilliseconds < config.ConnectTimeout);

            if (!success)
            {
                failure ??= new RedisConnectionException(
                    ConnectionFailureType.UnableToConnect,
                    CommandFlags.None,
                    $"Sentinel: Failed connecting to configured primary for service: {config.ServiceName}");
                if (config.AbortOnConnectFail)
                {
                    throw failure;
                }

                connection ??= await SeedConnectionAsync(failure).ForAwait();

                connection.LastException = failure;
                connection.Logger?.LogErrorSyncConnectTimeout(failure, failure.Message);
            }

            connection.IsSentinelManaged = true;

            // Attach to reconnect event to ensure proper connection to the new primary
            connection.ConnectionRestored += OnManagedConnectionRestored;

            // If we lost the connection, run a switch to a least try and get updated info about the primary
            connection.ConnectionFailed += OnManagedConnectionFailed;

            ConnectionMultiplexer? existing = null;
            lock (sentinelConnectionChildren)
            {
                if (sentinelConnectionChildren.TryGetValue(serviceName, out var child) && !child.IsDisposed)
                    existing = child;
                else
                    sentinelConnectionChildren[serviceName] = connection;
            }
            if (existing is not null)
            {
                connection.ConnectionRestored -= OnManagedConnectionRestored;
                connection.ConnectionFailed -= OnManagedConnectionFailed;
                try { await connection.DisposeAsync().ForAwait(); } catch { }
                connection = null;
                return existing;
            }

            // Perform the initial switchover
            var switchBlame = endpoints is { Count: > 0 } ? endpoints[0] : null;
            try
            {
                if (synchronousConnect && !config.AbortOnConnectFail && !connection.IsConnected)
                    ScheduleSentinelPrimaryReconnect(connection, switchBlame);
                else
                    await SwitchPrimaryAsync(switchBlame, connection, log).ForAwait();
            }
            catch when (!config.AbortOnConnectFail)
            {
                // AbortOnConnectFail=false means "hand me the multiplexer and keep trying" - which has to
                // hold however far we got. Gating this on !success would tear down a connection that had
                // *successfully* reached its primary just because the follow-up switchover threw, and throw
                // from a call the caller asked never to throw.
                // SwitchPrimaryAsync's finally has already scheduled recovery.
            }

            return connection;
        }
        catch
        {
            if (connection is not null)
            {
                connection.ConnectionRestored -= OnManagedConnectionRestored;
                connection.ConnectionFailed -= OnManagedConnectionFailed;
                lock (sentinelConnectionChildren)
                {
                    if (sentinelConnectionChildren.TryGetValue(serviceName, out var child) && ReferenceEquals(child, connection))
                    {
                        sentinelConnectionChildren.Remove(serviceName);
                    }
                }
                try { await connection.DisposeAsync().ForAwait(); } catch { }
            }
            throw;
        }

        // AbortOnConnectFail=false with no primary discovered: hand back a multiplexer seeded with whatever endpoints
        // the caller gave us (the sentinel addresses, via Connect), for SwitchPrimary to rebuild once discovery works.
        // A config with no endpoints of its own has nothing to seed with, and that is the discovery failure the caller
        // should see - not the "No nodes to consider" that ConnectImpl would throw at an empty endpoint list.
        Task<ConnectionMultiplexer> SeedConnectionAsync(RedisConnectionException discoveryFailure)
        {
            if (config.EndPoints.Count == 0)
            {
                throw discoveryFailure;
            }
            endpoints = config.EndPoints.Clone();
            return ConnectImplAsync(config, log, endpoints: endpoints, synchronousConnect: synchronousConnect);
        }
    }

    internal void OnManagedConnectionRestored(object? sender, ConnectionFailedEventArgs e)
    {
        if (sender is ConnectionMultiplexer connection)
        {
            // Failed/Restored are delivered on workers and may arrive out of order. Both ask for a fresh
            // sentinel view; only that view plus the current connection state can stop recovery.
            if (Volatile.Read(ref connection.sentinelPrimaryReconnectTimer) is null
                && Volatile.Read(ref connection._sentinelSwitchActive) == 0
                && connection.currentSentinelPrimaryEndPoint is EndPoint primary
                && !IsStalePrimaryView(connection, primary)) return;
            ScheduleSentinelPrimaryReconnect(connection, e.EndPoint);
        }
    }

    internal void OnManagedConnectionFailed(object? sender, ConnectionFailedEventArgs e)
    {
        if (sender is ConnectionMultiplexer connection)
        {
            ScheduleSentinelPrimaryReconnect(connection, e.EndPoint);
        }
    }

    private static void StopSentinelPrimaryReconnect(ConnectionMultiplexer connection)
        => Interlocked.Exchange(ref connection.sentinelPrimaryReconnectTimer, null)?.Dispose();

    private void ScheduleSentinelPrimaryReconnect(ConnectionMultiplexer connection, EndPoint? endPoint)
    {
        if (IsDisposed || connection.IsDisposed || connection.RawConfig.ServiceName is null)
        {
            StopSentinelPrimaryReconnect(connection);
            return;
        }
        if (Volatile.Read(ref connection.sentinelPrimaryReconnectTimer) is not null) return;

        // A one-shot timer, rearmed only after the asynchronous attempt finishes. Capture its own identity:
        // a finishing callback must never rearm a replacement timer installed by a concurrent failure.
        Timer? timer = null;
        timer = new Timer(_ => ReconnectAsync().RedisFireAndForget(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        if (Interlocked.CompareExchange(ref connection.sentinelPrimaryReconnectTimer, timer, null) is null)
        {
            try { timer.Change(TimeSpan.Zero, Timeout.InfiniteTimeSpan); }
            catch (ObjectDisposedException) { } // disposed/restored between publication and Change
        }
        else
        {
            timer.Dispose();
        }

        async Task ReconnectAsync()
        {
            try
            {
                if (IsDisposed || connection.IsDisposed)
                {
                    StopSentinelPrimaryReconnect(connection);
                    return;
                }
                await SwitchPrimaryAsync(endPoint, connection).ForAwait();
            }
            catch (Exception ex)
            {
                Trace("Sentinel primary reconnect failed: " + ex.Message, "Sentinel");
            }
            finally
            {
                if (ReferenceEquals(Volatile.Read(ref connection.sentinelPrimaryReconnectTimer), timer))
                {
                    try { timer!.Change(TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan); }
                    catch (ObjectDisposedException) { }
                }
            }
        }
    }

    /// <summary>
    /// Unhooks and forgets a disposed managed connection. Must be called under the
    /// <see cref="sentinelConnectionChildren"/> lock.
    /// </summary>
    private void ForgetDisposedChild(string serviceName, ConnectionMultiplexer child)
    {
        child.ConnectionFailed -= OnManagedConnectionFailed;
        child.ConnectionRestored -= OnManagedConnectionRestored;
        sentinelConnectionChildren.Remove(serviceName);
    }

    /// <summary>
    /// Re-resolves a managed primary that was seen demoted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every other recovery path needs a signal that this case does not produce: <c>+switch-master</c> can be
    /// missed (pub/sub delivery is not guaranteed, and the sentinel-clients guidance says not to rely on it),
    /// and the reconnect timer and the restore handler only run once a connection fails. A primary that is
    /// demoted in place, with nothing announced and nothing disconnected, is still noticed by the managed
    /// connection - its periodic <c>INFO replication</c> sees the role change - but then only re-elected
    /// among the nodes it already knows. When sentinel promoted a node at an address it has never seen,
    /// there is no primary among them, and nothing asked sentinel again.
    /// </para>
    /// <para>
    /// Runs on this sentinel connection's heartbeat. The check reads only local state, so sentinel is asked
    /// only when the managed connection itself reports the demotion, and at most one recovery per managed
    /// connection is in flight. A recovery that does not take - sentinel not yet updated, or a reconfigure
    /// already running - is simply retried on a later heartbeat, and the check clears itself once the
    /// sentinel-reported primary is connected as a primary.
    /// </para>
    /// </remarks>
    private void CheckManagedPrimariesDemoted()
    {
        List<ConnectionMultiplexer>? demoted = null;
        List<string>? disposed = null;
        lock (sentinelConnectionChildren)
        {
            foreach (var pair in sentinelConnectionChildren)
            {
                if (pair.Value.IsDisposed)
                {
                    (disposed ??= new List<string>()).Add(pair.Key);
                }
                else if (IsSentinelPrimaryDemoted(pair.Value))
                {
                    (demoted ??= new List<ConnectionMultiplexer>()).Add(pair.Value);
                }
            }

            if (disposed is not null)
            {
                foreach (var serviceName in disposed)
                {
                    ForgetDisposedChild(serviceName, sentinelConnectionChildren[serviceName]);
                }
            }
        }

        if (demoted is null) return;
        foreach (var child in demoted)
        {
            if (Interlocked.CompareExchange(ref child._demotedPrimaryRecoveryActive, 1, 0) != 0) continue;
            RecoverAsync(child).RedisFireAndForget();
        }

        async Task RecoverAsync(ConnectionMultiplexer child)
        {
            try
            {
                await RecoverDemotedPrimaryAsync(child).ForAwait();
            }
            catch (Exception ex)
            {
                // sentinel unreachable, or either connection disposed meanwhile: a later heartbeat looks again
                Trace("Demoted primary recovery failed: " + ex.Message, "Sentinel");
            }
            finally
            {
                Volatile.Write(ref child._demotedPrimaryRecoveryActive, 0);
            }
        }
    }

    /// <summary>
    /// One recovery attempt for a managed connection whose sentinel-reported primary was seen demoted.
    /// </summary>
    /// <remarks>
    /// Sentinel is asked first, because what to do depends on its answer, and a repeat of an unchanged answer
    /// must stay cheap - the attempt runs on every heartbeat until the view clears:
    /// <list type="bullet">
    /// <item><description>no answer: nothing to act on; the next heartbeat asks again.</description></item>
    /// <item><description>a node we hold as a connected replica: either sentinel has not failed over yet, or our
    /// view of that node's role is stale. Re-reading its role settles both, where a switch would reconfigure every
    /// node to elect a primary that may not exist - once per second for as long as sentinel lags. Where that read
    /// cannot be sent (<c>INFO</c> unavailable), fall through to the switch rather than ask sentinel forever.</description></item>
    /// <item><description>anything else: switch, exactly as a <c>+switch-master</c> announcement would have.
    /// Sentinel's answer is handed to <see cref="SwitchPrimaryAsync"/> so the switch acts on the answer these guards
    /// evaluated, rather than asking again and possibly acting on a different one.</description></item>
    /// </list>
    /// A reconfigure already running is left alone: <see cref="SwitchPrimaryAsync"/> claims the reconfigure gate
    /// before touching anything, so a lost race leaves no trace and a later heartbeat retries.
    /// </remarks>
    private async Task RecoverDemotedPrimaryAsync(ConnectionMultiplexer connection)
    {
        if (IsDisposed || connection.IsDisposed || connection.RawConfig.ServiceName is not string serviceName) return;

        // cheap early out before touching the topology; SwitchPrimaryAsync's claim is the authoritative check
        if (Volatile.Read(ref connection.activeConfigCause) is not null) return;

        if (await GetConfiguredPrimaryForServiceAsync(serviceName).ForAwait() is not SentinelPrimary primary) return;
        var reported = primary.EndPoint;
        if (connection.TryResolveServerEndPoint(reported) is { IsConnected: true, IsReplica: true } replica
            && await replica.CheckInfoReplicationAsync().ForAwait())
        {
            return;
        }

        // The view may have been put right while sentinel was answering.
        if (!IsSentinelPrimaryDemoted(connection)) return;

        if (await SwitchPrimaryAsync(connection.currentSentinelPrimaryEndPoint, connection, reportedPrimary: primary).ForAwait()
            && connection.TryResolveServerEndPoint(reported) is { IsConnected: true, IsReplica: false })
        {
            // As OnManagedConnectionRestored does: we are connected to a sentinel-approved primary again, so a
            // soft-failed connect's discovery failure should stop being folded into every timeout message.
            connection.LastException = null;
        }
    }

    /// <summary>
    /// Whether the primary sentinel last reported for <paramref name="connection"/> is connected and reporting
    /// itself as a replica, or was never added (a switch to it whose reconfigure did not run). A disconnected
    /// primary is not this case: that is the reconnect timer's job.
    /// </summary>
    private static bool IsSentinelPrimaryDemoted(ConnectionMultiplexer connection)
    {
        if (connection.IsDisposed
            || Volatile.Read(ref connection.sentinelPrimaryReconnectTimer) is not null
            || connection.currentSentinelPrimaryEndPoint is not EndPoint primary)
        {
            return false;
        }

        // a lookup, not GetServerEndPoint: that would create a server for an endpoint the connection does not have
        if (connection.TryResolveServerEndPoint(primary) is { } server)
        {
            return server.IsConnected && server.IsReplica;
        }

        // never added: a switch to it that was refused (another reconfigure held the gate) or threw. A missing server
        // alone is not enough evidence - the endpoint sentinel gave may simply be spelled differently from the
        // server's own (a DNS name against an address) - so only while nothing else is serving as the primary,
        // which keeps a healthy connection from switching on every heartbeat.
        foreach (var other in connection.GetServerSnapshot())
        {
            if (other.ServerType != ServerType.Sentinel && other.IsConnected && !other.IsReplica) return false;
        }
        return true;
    }

    internal sealed class SentinelPrimary(IServer sentinel, EndPoint endPoint)
    {
        internal IServer Sentinel { get; } = sentinel;
        internal EndPoint EndPoint { get; } = endPoint;
    }

    /// <summary>
    /// The primary sentinel holds for <paramref name="serviceName"/>: the first valid answer from any sentinel route.
    /// </summary>
    /// <remarks>
    /// https://redis.io/docs/latest/develop/reference/sentinel-clients/ Step 2 asks the sentinels for the address;
    /// an answer is taken only from a sentinel that holds that node as a healthy master (flags <c>master</c>, no
    /// <c>s_down</c>/<c>o_down</c>), which <c>SENTINEL MASTER</c> reports and <c>get-master-addr-by-name</c> does not -
    /// a sentinel still repeating a failed primary is passed over for one that already knows the new one. A stale
    /// but healthy-looking answer is still caught by the role check (Step 3) and asked again.
    /// </remarks>
    internal Task<SentinelPrimary?> GetConfiguredPrimaryForServiceAsync(string serviceName) =>
        QuerySentinelsAsync(async server =>
        {
            EndPoint? primary;
            try
            {
                primary = ParseValidPrimary(await server.SentinelMasterAsync(serviceName).ForAwait());
            }
            catch (RedisServerException ex) when (IsSentinelMasterUnsupported(ex))
            {
                primary = await server.SentinelGetMasterAddressByNameAsync(serviceName).ForAwait();
            }
            return primary is null ? null : new SentinelPrimary(server, primary);
        });

    internal static bool IsSentinelMasterUnsupported(RedisServerException ex) =>
        ex.Message.IndexOf("unknown command", StringComparison.OrdinalIgnoreCase) >= 0
        || ex.Message.IndexOf("unknown subcommand", StringComparison.OrdinalIgnoreCase) >= 0
        || ex.Message.IndexOf("unknown sentinel subcommand", StringComparison.OrdinalIgnoreCase) >= 0
        || ex.Message.IndexOf("unrecognized sentinel command", StringComparison.OrdinalIgnoreCase) >= 0;

    private static async Task<SentinelReplica[]?> GetReplicasForServiceAsync(string serviceName, SentinelPrimary primary)
    {
        try
        {
            // Use the same sentinel route whose primary answer won; a second race could combine unrelated views.
            var rows = await primary.Sentinel.SentinelReplicasAsync(serviceName).ForAwait();
            var replicas = new List<SentinelReplica>(rows.Length);
            foreach (var row in rows)
            {
                if (ParseSentinelAddress(row, out var down) is EndPoint address) replicas.Add(new SentinelReplica(address, down));
            }
            return replicas.ToArray();
        }
        catch (Exception) { return null; }
    }

    /// <summary>
    /// Asks every sentinel at once and takes the first answer, as the parallel query this replaces did - but through
    /// the asynchronous API: a request waiting on a slow or dead sentinel holds no thread-pool worker.
    /// </summary>
    private Task<T?> QuerySentinelsAsync<T>(Func<IServer, Task<T?>> query) where T : class
    {
        return FirstSentinelResultAsync(GetSentinelsForQuery().Select(QueryAsync).ToArray(), AsyncTimeoutMilliseconds);

        async Task<T?> QueryAsync(ServerEndPoint server)
        {
            try { return await query(GetServer(server.EndPoint)).ForAwait(); }
            catch (Exception) { return null; }
        }
    }

    // sentinel-clients Step 1 tries the sentinels one by one, moving the one that answered to the front. Every route
    // is asked at once instead - the requests are asynchronous and hold no worker, and a Service address and the
    // advertised pod address of the same sentinel need not be told apart - connected routes first and routes
    // sentinel itself reports down last, so a dead route can delay nothing and the first valid answer wins.
    private ServerEndPoint[] GetSentinelsForQuery()
    {
        var down = Volatile.Read(ref _sentinelDownRoutes);
        return OrderSentinelQueryTargets(
            _serverSnapshot.Where(static s => s.ServerType == ServerType.Sentinel && !s.IsDisposed).ToArray(),
            static s => s.IsConnected,
            s => down.Contains(s.EndPoint));
    }

    internal static T[] OrderSentinelQueryTargets<T>(T[] targets, Func<T, bool> connected, Func<T, bool> reportedDown)
        => targets.OrderBy(t => connected(t) ? 0 : reportedDown(t) ? 2 : 1).ToArray(); // OrderBy is stable

    internal static bool IsSentinelNodeDown(string? flags) => flags is not null
        && flags.Split(',').Any(static f => f is "s_down" or "o_down" or "disconnected");

    // a primary is unusable only when sentinel holds it down; "disconnected" is a link sentinel is re-establishing
    internal static bool IsSentinelPrimaryDown(string? flags) => flags is not null
        && flags.Split(',').Any(static f => f is "s_down" or "o_down");

    /// <summary>
    /// A replica as sentinel reports it. Sentinel never forgets a replica (sentinel docs: "Sentinels never forget
    /// about replicas of a given master" until <c>SENTINEL RESET</c>), so a failed one stays listed with down flags.
    /// </summary>
    internal readonly struct SentinelReplica(EndPoint endPoint, bool down)
    {
        public EndPoint EndPoint { get; } = endPoint;
        public bool Down { get; } = down;
    }

    internal static EndPoint? ParseValidPrimary(KeyValuePair<string, string>[] row)
    {
        var fields = row.ToDictionary(static p => p.Key, static p => p.Value);
        if (!fields.TryGetValue("flags", out var flags) || !flags.Split(',').Contains("master") || IsSentinelPrimaryDown(flags)) return null;
        return ParseSentinelAddress(fields);
    }

    private static EndPoint? ParseSentinelAddress(KeyValuePair<string, string>[] row, out bool down)
    {
        var fields = row.ToDictionary(static p => p.Key, static p => p.Value);
        down = fields.TryGetValue("flags", out var flags) && IsSentinelNodeDown(flags);
        return ParseSentinelAddress(fields);
    }

    private static EndPoint? ParseSentinelAddress(Dictionary<string, string> fields)
    {
        if (fields.TryGetValue("ip", out var host) && fields.TryGetValue("port", out var port)
            && Format.TryParseEndPoint(host, port, out var address)) return address;
        return null;
    }

    // Every request observes its own failure, including the requests still outstanding when a faster
    // sentinel answers. One deadline covers the whole race, rather than a timeout per losing request.
    // All requests use the asynchronous API: waiting for a slow/dead sentinel consumes no worker.
    internal static async Task<T?> FirstSentinelResultAsync<T>(Task<T?>[] requests, int timeoutMilliseconds) where T : class
    {
        using var timeout = new CancellationTokenSource();
        var deadline = Task.Delay(timeoutMilliseconds, timeout.Token);
        var pending = new List<Task>(requests.Length + 1) { deadline };
        foreach (var request in requests) pending.Add(request.ObserveErrors());
        try
        {
            while (pending.Count > 1)
            {
                var completed = await Task.WhenAny(pending).ForAwait();
                if (ReferenceEquals(completed, deadline)) break;
                pending.Remove(completed);
                try
                {
                    if (await ((Task<T?>)completed).ForAwait() is T result) return result;
                }
                catch (Exception) { } // another sentinel may still have an answer
            }
            return null;
        }
        finally
        {
            timeout.Cancel();
        }
    }

    /// <summary>
    /// Switches the SentinelMasterConnection over to a new primary, without blocking a worker.
    /// </summary>
    /// <remarks>
    /// The reconnect timer, the demotion heartbeat, the restore handler and <c>+switch-master</c> all call this. A
    /// per-connection gate covers discovery as well as reconfiguration, so they cannot overlap; a switch requested while
    /// one is running is not dropped, but recorded and replayed once it finishes. The reconnect timer is stopped here,
    /// and only here, once the connection is on the sentinel-reported primary with no stale view left.
    /// </remarks>
    /// <param name="switchBlame">The endpoint responsible for the switch.</param>
    /// <param name="connection">The connection that should be switched over to a new primary endpoint.</param>
    /// <param name="writer">The writer to log to, if any.</param>
    /// <param name="reportedPrimary">The primary sentinel already reported, when the caller has just asked it;
    /// otherwise sentinel is queried here.</param>
    /// <returns><c>true</c> when a switch ran (the endpoint set and/or roles were reconfigured); <c>false</c> when
    /// the view was already correct, another switch is running, or another reconfigure holds the gate - the callers
    /// that retry (the reconnect timer, the demotion heartbeat) get a later attempt either way.</returns>
    internal async Task<bool> SwitchPrimaryAsync(EndPoint? switchBlame, ConnectionMultiplexer connection, TextWriter? writer = null, SentinelPrimary? reportedPrimary = null)
    {
        if (IsDisposed || connection.IsDisposed) return false;
        if (connection.RawConfig.ServiceName is null)
        {
            StopSentinelPrimaryReconnect(connection);
            return false;
        }
        if (Interlocked.CompareExchange(ref connection._sentinelSwitchActive, 1, 0) != 0)
        {
            Interlocked.Exchange(ref connection._sentinelSwitchPending, 1);
            ScheduleSentinelPrimaryReconnect(connection, switchBlame);
            return false;
        }

        var serviceName = connection.RawConfig.ServiceName;
        bool refreshAddresses = connection.currentSentinelPrimaryEndPoint is null;
        var logger = Logger.With(writer);
        bool healthy = false, claimed = false, handedOver = false;
        EndPoint? confirmedPrimary = null;
        try
        {
            if (serviceName is null)
            {
                logger?.LogInformationServiceNameNotDefined();
                return false;
            }

            // Get new primary - try twice (unless the caller already holds sentinel's answer)
            var primary = reportedPrimary
                ?? await GetConfiguredPrimaryForServiceAsync(serviceName).ForAwait()
                ?? await GetConfiguredPrimaryForServiceAsync(serviceName).ForAwait()
                ?? throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, CommandFlags.None, $"Sentinel: Failed connecting to switch primary for service: {serviceName}");
            var newPrimaryEndPoint = primary.EndPoint;
            connection.currentSentinelPrimaryEndPoint = confirmedPrimary = newPrimaryEndPoint;

            // Refresh replicas even when the primary is already known. Otherwise an unreachable old replica
            // (or an old primary) stays in the snapshot forever after a node returns at a different address.
            var replicas = await GetReplicasForServiceAsync(serviceName, primary).ForAwait();

            // Claim the reconfigure gate BEFORE reshaping the connection. ReconfigureAsync refuses to run while
            // another reconfigure holds this gate, so rewriting the endpoint list and retiring servers first would
            // leave the connection reshaped but unconfigured whenever we lose that race - and two concurrent
            // switches could interleave their endpoint rewrites. Claim first and a lost race touches nothing.
            string cause = $"Primary switch {serviceName}";
            if (!connection.TryClaimReconfigure(cause))
            {
                Trace($"Primary switch for {serviceName} skipped; reconfiguration already in progress");
                return false;
            }
            claimed = true;

            bool unknownPrimary = !connection.servers.Contains(newPrimaryEndPoint);
            bool stale = IsStalePrimaryView(connection, newPrimaryEndPoint);
            bool changed = false;
            if (replicas is not null)
            {
                // Down flags (s_down/o_down/disconnected) decide only whether a node not yet held is added. A node
                // this connection already holds stays whatever one sentinel's momentary view says - sentinel reports
                // the demoted primary disconnected while it reconnects to it, and a live connection outranks that.
                // Only a disconnected node missing from a nonempty reply by the same sentinel may leave;
                // an incomplete or empty view must not retire live nodes.
                var desired = new EndPointCollection { newPrimaryEndPoint };
                foreach (var held in connection.EndPoints)
                {
                    if (replicas.Length == 0 || connection.TryResolveServerEndPoint(held) is { IsConnected: true }) desired.TryAdd(held);
                }
                foreach (var replica in replicas)
                {
                    if (!replica.Down || connection.EndPoints.Contains(replica.EndPoint)) desired.TryAdd(replica.EndPoint);
                }
                changed = !new HashSet<EndPoint>(connection.EndPoints).SetEquals(desired);
                if (changed)
                {
                    connection.EndPoints.Clear();
                    foreach (var endpoint in desired) connection.EndPoints.TryAdd(endpoint);
                    await RetireServersAbsentFromEndPointsAsync(connection, logger).ForAwait();
                }
            }
            else if (unknownPrimary)
            {
                // A failed replica query is not an empty topology. Keep the old nodes until discovery works.
                connection.EndPoints.TryAdd(newPrimaryEndPoint);
                changed = true;
            }

            bool switched = changed || stale;
            refreshAddresses |= switched;
            if (switched)
            {
                // Step 3: a new node reads its role during handshake. For a known primary, reconfigureAll forces
                // INFO on connected nodes too; an unknown primary can leave cached roles on old nodes until the
                // next recovery attempt. Any remaining replica/second-primary view keeps recovery running.
                Trace($"Switching primary to {newPrimaryEndPoint}");
                handedOver = true; // from here the claim is ReconfigureAsync's to release, however it exits
                await connection.ReconfigureAsync(
                    first: false,
                    reconfigureAll: !unknownPrimary,
                    log: logger,
                    blame: switchBlame,
                    cause: cause,
                    publishReconfigure: false,
                    publishReconfigureFlags: CommandFlags.PreferMaster,
                    claimed: true).ForAwait();
            }

            healthy = !IsStalePrimaryView(connection, newPrimaryEndPoint);
            if (healthy) connection.LastException = null;
            return switched;
        }
        finally
        {
            // not handed over (view already correct, or we threw before the reconfigure): release it ourselves
            if (claimed && !handedOver) connection.ReleaseReconfigureClaim();
            if (healthy && Volatile.Read(ref connection._sentinelSwitchPending) == 0)
            {
                StopSentinelPrimaryReconnect(connection);
                // A failure delivered while the old timer was still published may not have installed a
                // replacement. Recheck after removing it so a concurrent disconnect cannot lose recovery.
                healthy = confirmedPrimary is not null && !IsStalePrimaryView(connection, confirmedPrimary);
            }
            Volatile.Write(ref connection._sentinelSwitchActive, 0);
            if (Interlocked.Exchange(ref connection._sentinelSwitchPending, 0) != 0 || !healthy)
                ScheduleSentinelPrimaryReconnect(connection, switchBlame);
            // Address discovery runs on its own; it must not widen the switch gate.
            if (refreshAddresses && serviceName is not null) UpdateSentinelAddressListAsync(serviceName).RedisFireAndForget();
        }
    }

    /// <summary>
    /// Retires every server the rebuilt endpoint set no longer names.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Retired, not merely forgotten. Emptying the server dictionary leaves the <see cref="ServerEndPoint"/>
    /// in the multiplexer's snapshot, and the snapshot - not the dictionary - is what
    /// <see cref="ServerSelectionStrategy"/> chooses from and what <see cref="GetEndPoints"/> reports: the
    /// dropped server stays connected and selectable, so commands can still be routed to a node sentinel no
    /// longer names. A later lookup then builds a *second* server for the same address, because the
    /// dictionary missed while the snapshot still held the first.
    /// </para>
    /// <para>
    /// <see cref="RetireServerAsync"/> is the one spelling of retirement, so the drain is honoured here as
    /// well: a dropped node finishes what it owes rather than having its in-flight commands abandoned.
    /// </para>
    /// </remarks>
    private static async Task RetireServersAbsentFromEndPointsAsync(ConnectionMultiplexer connection, ILogger? log)
    {
        var drop = connection._serverSnapshot.Where(s => !s.IsDisposed && !connection.EndPoints.Contains(s.EndPoint)).ToArray();
        foreach (var server in drop)
        {
            // short drain: the reconfigure that follows is the part that matters for getting traffic moving again
            await connection.RetireServerAsync(server, "no longer named by sentinel", TimeSpan.FromSeconds(1), log).ForAwait();
        }
    }

    /// <summary>
    /// Determines whether the multiplexer's cached view of the topology disagrees with the
    /// sentinel-reported primary for a known endpoint, and therefore needs a full reconfigure.
    /// </summary>
    /// <remarks>
    /// This is the self-clearing guard that keeps the per-second <see cref="OnManagedConnectionFailed"/>
    /// timer from storming reconfigures: once a switch has succeeded, a full reconfigure has refreshed
    /// the new primary's role (<see cref="ServerEndPoint.IsReplica"/> becomes <c>false</c>, it is
    /// connected, and no other node is still seen as a primary), so every subsequent tick observes a
    /// healthy, correct primary and returns <c>false</c>.
    /// </remarks>
    /// <param name="connection">The managed connection whose view is being validated.</param>
    /// <param name="newPrimaryEndPoint">The primary endpoint reported by sentinel (already known to the connection).</param>
    private static bool IsStalePrimaryView(ConnectionMultiplexer connection, EndPoint newPrimaryEndPoint)
    {
        var newPrimaryServer = connection.TryResolveServerEndPoint(newPrimaryEndPoint);

        // We do not know this endpoint yet, or we still think the sentinel-reported primary is a
        // replica, or its interactive connection is down: our view is stale. Subscription recovery is
        // handled by the normal bridges; it must not keep primary discovery polling a healthy data node.
        if (newPrimaryServer is null || newPrimaryServer.IsReplica || !newPrimaryServer.IsConnected)
        {
            return true;
        }

        // The sentinel-reported primary looks correct locally, but double-check that no *other*
        // node is still being treated as a connected primary (a demoted node whose role has not
        // yet been refreshed). If one exists, our view is split and needs a full reconfigure.
        foreach (var server in connection.GetServerSnapshot())
        {
            if (server.ServerType != ServerType.Sentinel
                && server.IsConnected
                && !server.IsReplica
                && !Equals(server.EndPoint, newPrimaryEndPoint))
            {
                return true;
            }
        }

        return false;
    }

    internal async Task UpdateSentinelAddressListAsync(string serviceName)
    {
        lock (_sentinelAddressUpdatesPending)
        {
            if (IsDisposed) return;
            _sentinelAddressUpdatesPending.Add(serviceName);
            if (_sentinelAddressUpdateActive != 0) return;
            _sentinelAddressUpdateActive = 1;
        }
        try
        {
            while (!IsDisposed)
            {
                lock (_sentinelAddressUpdatesPending)
                {
                    if (_sentinelAddressUpdatesPending.Count == 0) break;
                    serviceName = _sentinelAddressUpdatesPending.First();
                    _sentinelAddressUpdatesPending.Remove(serviceName);
                }
                var peers = await QuerySentinelsAsync(async server =>
                    (KeyValuePair<string, string>[][]?)await server.SentinelSentinelsAsync(serviceName).ForAwait()).ForAwait();
                if (peers is null) continue;

                var down = new HashSet<EndPoint>();
                bool hasNew = false;
                // https://valkey.io/topics/sentinel-clients/: append unknown ip:port pairs, retaining all old routes.
                // Add before claiming reconfigure, so a busy claim cannot lose the discovery result.
                foreach (var peer in peers)
                {
                    if (ParseSentinelAddress(peer, out var isDown) is not EndPoint address) continue;
                    hasNew |= EndPoints.TryAdd(address);
                    if (isDown) down.Add(address);
                }
                Volatile.Write(ref _sentinelDownRoutes, down);
                if (!hasNew) continue;

                const string cause = "Updating Sentinel List";
                while (!IsDisposed)
                {
                    if (TryClaimReconfigure(cause))
                    {
                        await ReconfigureAsync(first: false, reconfigureAll: true, Logger, EndPoints[0], cause, false, claimed: true).ForAwait();
                        break;
                    }
                    // Coalesce later announcements while waiting for the owner of the claim; no worker is held.
                    await Task.Delay(1000).ForAwait();
                }
            }
        }
        finally
        {
            string? pending;
            lock (_sentinelAddressUpdatesPending)
            {
                _sentinelAddressUpdateActive = 0;
                pending = !IsDisposed ? _sentinelAddressUpdatesPending.FirstOrDefault() : null;
                if (IsDisposed) _sentinelAddressUpdatesPending.Clear();
            }
            if (pending is not null) UpdateSentinelAddressListAsync(pending).RedisFireAndForget();
        }
    }
}
