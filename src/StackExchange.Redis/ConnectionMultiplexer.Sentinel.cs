using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Pipelines.Sockets.Unofficial;

namespace StackExchange.Redis;

public partial class ConnectionMultiplexer
{
    internal EndPoint? currentSentinelPrimaryEndPoint;
    internal Timer? sentinelPrimaryReconnectTimer;
    internal Dictionary<string, ConnectionMultiplexer> sentinelConnectionChildren = new Dictionary<string, ConnectionMultiplexer>();
    internal ConnectionMultiplexer? sentinelConnection;

    /// <summary>
    /// Initializes the connection as a Sentinel connection and adds the necessary event handlers to track changes to the managed primaries.
    /// </summary>
    /// <param name="logProxy">The writer to log to, if any.</param>
    internal void InitializeSentinel(LogProxy? logProxy)
    {
        if (ServerSelectionStrategy.ServerType != ServerType.Sentinel)
        {
            return;
        }

        // Subscribe to sentinel change events
        ISubscriber sub = GetSubscriber();

        if (sub.SubscribedEndpoint("+switch-master") == null)
        {
            sub.Subscribe("+switch-master", (__, message) =>
            {
                string[] messageParts = ((string)message!).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                // We don't care about the result of this - we're just trying
                _ = Format.TryParseEndPoint(string.Format("{0}:{1}", messageParts[1], messageParts[2]), out var switchBlame);

                lock (sentinelConnectionChildren)
                {
                    // Switch the primary if we have connections for that service
                    if (sentinelConnectionChildren.ContainsKey(messageParts[0]))
                    {
                        ConnectionMultiplexer child = sentinelConnectionChildren[messageParts[0]];

                        // Is the connection still valid?
                        if (child.IsDisposed)
                        {
                            child.ConnectionFailed -= OnManagedConnectionFailed;
                            child.ConnectionRestored -= OnManagedConnectionRestored;
                            sentinelConnectionChildren.Remove(messageParts[0]);
                        }
                        else
                        {
                            SwitchPrimary(switchBlame, sentinelConnectionChildren[messageParts[0]]);
                        }
                    }
                }
            }, CommandFlags.FireAndForget);
        }

        // If we lose connection to a sentinel server,
        // we need to reconfigure to make sure we still have a subscription to the +switch-master channel
        ConnectionFailed += (sender, e) =>
            // Reconfigure to get subscriptions back online
            ReconfigureAsync(first: false, reconfigureAll: true, logProxy, e.EndPoint, "Lost sentinel connection", false).Wait();

        // Subscribe to new sentinels being added
        if (sub.SubscribedEndpoint("+sentinel") == null)
        {
            sub.Subscribe("+sentinel", (_, message) =>
            {
                string[] messageParts = ((string)message!).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                UpdateSentinelAddressList(messageParts[0]);
            }, CommandFlags.FireAndForget);
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
        SocketConnection.AssertDependencies();
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
        SocketConnection.AssertDependencies();
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
        var sentinelConnection = SentinelConnect(configuration, log);

        var muxer = sentinelConnection.GetSentinelMasterConnection(configuration, log);
        // Set reference to sentinel connection so that we can dispose it
        muxer.sentinelConnection = sentinelConnection;

        return muxer;
    }

    /// <summary>
    /// Create a new <see cref="ConnectionMultiplexer"/> instance that connects to a sentinel server, discovers the current primary server
    /// for the specified <see cref="ConfigurationOptions.ServiceName"/> in the config and returns a managed connection to the current primary server.
    /// </summary>
    /// <param name="configuration">The configuration options to use for this multiplexer.</param>
    /// <param name="log">The <see cref="TextWriter"/> to log to.</param>
    private static async Task<ConnectionMultiplexer> SentinelPrimaryConnectAsync(ConfigurationOptions configuration, TextWriter? log = null)
    {
        var sentinelConnection = await SentinelConnectAsync(configuration, log).ForAwait();

        var muxer = sentinelConnection.GetSentinelMasterConnection(configuration, log);
        // Set reference to sentinel connection so that we can dispose it
        muxer.sentinelConnection = sentinelConnection;

        return muxer;
    }

    /// <summary>
    /// Returns a managed connection to the primary server indicated by the <see cref="ConfigurationOptions.ServiceName"/> in the config.
    /// </summary>
    /// <param name="config">The configuration to be used when connecting to the primary.</param>
    /// <param name="log">The writer to log to, if any.</param>
    public ConnectionMultiplexer GetSentinelMasterConnection(ConfigurationOptions config, TextWriter? log = null)
    {
        if (ServerSelectionStrategy.ServerType != ServerType.Sentinel)
        {
            throw new RedisConnectionException(ConnectionFailureType.UnableToConnect,
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

        var sw = ValueStopwatch.StartNew();
        do
        {
            // Sentinel has some fun race behavior internally - give things a few shots for a quicker overall connect.
            const int queryAttempts = 2;

            EndPoint? newPrimaryEndPoint = null;
            for (int i = 0; i < queryAttempts && newPrimaryEndPoint is null; i++)
            {
                newPrimaryEndPoint = GetConfiguredPrimaryForService(serviceName);
            }

            if (newPrimaryEndPoint is null)
            {
                throw new RedisConnectionException(ConnectionFailureType.UnableToConnect,
                    $"Sentinel: Failed connecting to configured primary for service: {config.ServiceName}");
            }

            EndPoint[]? replicaEndPoints = null;
            for (int i = 0; i < queryAttempts && replicaEndPoints is null; i++)
            {
                replicaEndPoints = GetReplicasForService(serviceName);
            }

            // Replace the primary endpoint, if we found another one
            // If not, assume the last state is the best we have and minimize the race
            if (config.EndPoints.Count == 1)
            {
                config.EndPoints[0] = newPrimaryEndPoint;
            }
            else
            {
                config.EndPoints.Clear();
                config.EndPoints.TryAdd(newPrimaryEndPoint);
            }

            if (replicaEndPoints is not null)
            {
                foreach (var replicaEndPoint in replicaEndPoints)
                {
                    config.EndPoints.TryAdd(replicaEndPoint);
                }
            }

            connection = ConnectImpl(config, log);

            // verify role is primary according to:
            // https://redis.io/topics/sentinel-clients
            if (connection.GetServer(newPrimaryEndPoint)?.Role()?.Value == RedisLiterals.master)
            {
                success = true;
                break;
            }

            Thread.Sleep(100);
        } while (sw.ElapsedMilliseconds < config.ConnectTimeout);

        if (!success)
        {
            throw new RedisConnectionException(ConnectionFailureType.UnableToConnect,
                $"Sentinel: Failed connecting to configured primary for service: {config.ServiceName}");
        }

        // Attach to reconnect event to ensure proper connection to the new primary
        connection.ConnectionRestored += OnManagedConnectionRestored;

        // If we lost the connection, run a switch to a least try and get updated info about the primary
        connection.ConnectionFailed += OnManagedConnectionFailed;

        lock (sentinelConnectionChildren)
        {
            sentinelConnectionChildren[serviceName] = connection;
        }

        // Perform the initial switchover
        SwitchPrimary(EndPoints[0], connection, log);

        return connection;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Roslynator", "RCS1075:Avoid empty catch clause that catches System.Exception.", Justification = "We don't care.")]
    internal void OnManagedConnectionRestored(object? sender, ConnectionFailedEventArgs e)
    {
        if (sender is not ConnectionMultiplexer connection)
        {
            return; // This should never happen - called from non-nullable ConnectionFailedEventArgs
        }

        var oldTimer = Interlocked.Exchange(ref connection.sentinelPrimaryReconnectTimer, null);
        oldTimer?.Dispose();

        try
        {
            // Run a switch to make sure we have update-to-date
            // information about which primary we should connect to
            SwitchPrimary(e.EndPoint, connection);

            try
            {
                // Verify that the reconnected endpoint is a primary,
                // and the correct one otherwise we should reconnect
                if (connection.GetServer(e.EndPoint).IsReplica || e.EndPoint != connection.currentSentinelPrimaryEndPoint)
                {
                    // This isn't a primary, so try connecting again
                    SwitchPrimary(e.EndPoint, connection);
                }
            }
            catch (Exception)
            {
                // If we get here it means that we tried to reconnect to a server that is no longer
                // considered a primary by Sentinel and was removed from the list of endpoints.

                // If we caught an exception, we may have gotten a stale endpoint
                // we are not aware of, so retry
                SwitchPrimary(e.EndPoint, connection);
            }
        }
        catch (Exception)
        {
            // Log, but don't throw in an event handler
            // TODO: Log via new event handler? a la ConnectionFailed?
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Roslynator", "RCS1075:Avoid empty catch clause that catches System.Exception.", Justification = "We don't care.")]
    internal void OnManagedConnectionFailed(object? sender, ConnectionFailedEventArgs e)
    {
        if (sender is not ConnectionMultiplexer connection)
        {
            return; // This should never happen - called from non-nullable ConnectionFailedEventArgs
        }

        // Periodically check to see if we can reconnect to the proper primary.
        // This is here in case we lost our subscription to a good sentinel instance
        // or if we miss the published primary change.
        if (connection.sentinelPrimaryReconnectTimer == null)
        {
            connection.sentinelPrimaryReconnectTimer = new Timer(_ =>
            {
                try
                {
                    // Attempt, but do not fail here
                    SwitchPrimary(e.EndPoint, connection);
                }
                catch (Exception)
                {
                }
                finally
                {
                    try
                    {
                        connection.sentinelPrimaryReconnectTimer?.Change(TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
                    }
                    catch (ObjectDisposedException)
                    {
                        // If we get here the managed connection was restored and the timer was
                        // disposed by another thread, so there's no need to run the timer again.
                    }
                }
            }, null, TimeSpan.Zero, Timeout.InfiniteTimeSpan);
        }
    }

    internal EndPoint? GetConfiguredPrimaryForService(string serviceName) =>
        GetServerSnapshot()
            .ToArray()
            .Where(s => s.ServerType == ServerType.Sentinel)
            .AsParallel()
            .Select(s =>
            {
                try { return GetServer(s.EndPoint).SentinelGetMasterAddressByName(serviceName); }
                catch { return null; }
            })
            .FirstOrDefault(r => r != null);

    internal EndPoint[]? GetReplicasForService(string serviceName) =>
        GetServerSnapshot()
            .ToArray()
            .Where(s => s.ServerType == ServerType.Sentinel)
            .AsParallel()
            .Select(s =>
            {
                try { return GetServer(s.EndPoint).SentinelGetReplicaAddresses(serviceName); }
                catch { return null; }
            })
            .FirstOrDefault(r => r != null);

    /// <summary>
    /// Switches the SentinelMasterConnection over to a new primary.
    /// </summary>
    /// <param name="switchBlame">The endpoint responsible for the switch.</param>
    /// <param name="connection">The connection that should be switched over to a new primary endpoint.</param>
    /// <param name="log">The writer to log to, if any.</param>
    internal void SwitchPrimary(EndPoint? switchBlame, ConnectionMultiplexer connection, TextWriter? log = null)
    {
        if (log == null) log = TextWriter.Null;

        using (var logProxy = LogProxy.TryCreate(log))
        {
            if (connection.RawConfig.ServiceName is not string serviceName)
            {
                logProxy?.WriteLine("Service name not defined.");
                return;
            }

            // Get new primary - try twice
            EndPoint newPrimaryEndPoint = GetConfiguredPrimaryForService(serviceName)
                                        ?? GetConfiguredPrimaryForService(serviceName)
                                        ?? throw new RedisConnectionException(ConnectionFailureType.UnableToConnect,
                                            $"Sentinel: Failed connecting to switch primary for service: {serviceName}");

            connection.currentSentinelPrimaryEndPoint = newPrimaryEndPoint;

            if (!connection.servers.Contains(newPrimaryEndPoint))
            {
                EndPoint[]? replicaEndPoints = GetReplicasForService(serviceName)
                                            ?? GetReplicasForService(serviceName);

                connection.servers.Clear();
                connection.EndPoints.Clear();
                connection.EndPoints.TryAdd(newPrimaryEndPoint);
                if (replicaEndPoints is not null)
                {
                    foreach (var replicaEndPoint in replicaEndPoints)
                    {
                        connection.EndPoints.TryAdd(replicaEndPoint);
                    }
                }
                Trace($"Switching primary to {newPrimaryEndPoint}");
                // Trigger a reconfigure
                connection.ReconfigureAsync(first: false, reconfigureAll: false, logProxy, switchBlame,
                    $"Primary switch {serviceName}", false, CommandFlags.PreferMaster).Wait();

                UpdateSentinelAddressList(serviceName);
            }
        }
    }

    internal void UpdateSentinelAddressList(string serviceName)
    {
        var firstCompleteRequest = GetServerSnapshot()
                                    .ToArray()
                                    .Where(s => s.ServerType == ServerType.Sentinel)
                                    .AsParallel()
                                    .Select(s =>
                                    {
                                        try { return GetServer(s.EndPoint).SentinelGetSentinelAddresses(serviceName); }
                                        catch { return null; }
                                    })
                                    .FirstOrDefault(r => r != null);

        // Ignore errors, as having an updated sentinel list is not essential
        if (firstCompleteRequest == null)
            return;

        bool hasNew = false;
        foreach (EndPoint newSentinel in firstCompleteRequest.Where(x => !EndPoints.Contains(x)))
        {
            hasNew = true;
            EndPoints.TryAdd(newSentinel);
        }

        if (hasNew)
        {
            // Reconfigure the sentinel multiplexer if we added new endpoints
            ReconfigureAsync(first: false, reconfigureAll: true, null, EndPoints[0], "Updating Sentinel List", false).Wait();
        }
    }
}
