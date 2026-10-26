using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using RESPite.Messages;
using StackExchange.Redis.Server;
using StackExchange.Redis.Configuration;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// A sentinel-managed connection whose primary is demoted while every connection stays up. Sentinel's own
/// recovery levers - <c>+switch-master</c>, and killing the demoted node's clients - are exactly what is
/// missing here, so the only signal the client gets is the demoted node reporting itself as a replica.
/// </summary>
public class SentinelPrimaryDemotionUnitTests(ITestOutputHelper log)
{
    private const string ServiceName = "mymaster";

    // documentation-range addresses (RFC 5737): a node that is not in-process can never be reached by accident
    private static readonly EndPoint PrimaryA = new IPEndPoint(IPAddress.Parse("192.0.2.1"), 6379);
    private static readonly EndPoint ReplicaB = new IPEndPoint(IPAddress.Parse("192.0.2.2"), 6379);
    private static readonly EndPoint NewNodeC = new IPEndPoint(IPAddress.Parse("192.0.2.3"), 6379);
    private static readonly EndPoint SentinelEndPoint = new IPEndPoint(IPAddress.Parse("192.0.2.10"), 26379);
    private static readonly EndPoint SecondSentinelEndPoint = new IPEndPoint(IPAddress.Parse("192.0.2.11"), 26379);

    [Fact]
    public async Task DemotedPrimaryFollowsSentinelToPrimaryAtUnknownAddress()
    {
        using var data = new InProcessTestServer(log, PrimaryA);
        data.AddReplicaNode(ReplicaB, PrimaryA);
        using var sentinel = new InProcessSentinelServer(data, ServiceName, PrimaryA, SentinelEndPoint, log);

        await using var conn = await ConnectionMultiplexer.ConnectAsync(CreateConfig(data, sentinel));
        Assert.Equal([PrimaryA], ConnectedPrimaries(conn));

        // the primary moves to a node the client has never been told about - a pod that came back on a new
        // address - and the client misses the announcement; no connection is dropped
        FailOverToNewNode(data);
        sentinel.Primary = NewNodeC;

        await AssertPrimaryBecomesAsync(conn, NewNodeC);
    }

    [Fact]
    public async Task DemotedPrimaryDoesNotReconfigureWhileSentinelStillNamesIt()
    {
        using var data = new InProcessTestServer(log, PrimaryA);
        data.AddReplicaNode(ReplicaB, PrimaryA);
        using var sentinel = new InProcessSentinelServer(data, ServiceName, PrimaryA, SentinelEndPoint, log);

        await using var conn = await ConnectionMultiplexer.ConnectAsync(CreateConfig(data, sentinel));
        int configurationChanges = 0;
        conn.ConfigurationChanged += (_, _) => Interlocked.Increment(ref configurationChanges);

        // sentinel has not caught up: it still names the node that is now a replica
        FailOverToNewNode(data);
        await UntilAsync(() => ConnectedPrimaries(conn).Length == 0, "the demotion was not noticed");

        // The property under test: the client keeps asking sentinel, but does not reconfigure every node to
        // elect a primary that does not exist. Anchor on the sentinel polls themselves rather than wall-clock
        // quiet: require a stretch of several polls with zero configuration changes. A straggler reconfigure
        // from the demotion itself re-baselines the stretch; the per-heartbeat reconfigure storm this guards
        // against would raise a change on every poll, so no clean stretch would ever complete.
        int changesBaseline = Volatile.Read(ref configurationChanges);
        int queriesBaseline = sentinel.PrimaryQueries;
        await UntilAsync(
            () =>
            {
                int changesNow = Volatile.Read(ref configurationChanges);
                if (changesNow != changesBaseline)
                {
                    changesBaseline = changesNow;
                    queriesBaseline = sentinel.PrimaryQueries;
                    return false;
                }
                return sentinel.PrimaryQueries >= queriesBaseline + 3;
            },
            "no stretch of sentinel polls without a reconfigure");
        log.WriteLine($"clean stretch: {sentinel.PrimaryQueries - queriesBaseline} sentinel polls, configuration changes still {changesBaseline}");

        // and once sentinel does catch up, the connection follows it
        sentinel.Primary = NewNodeC;
        await AssertPrimaryBecomesAsync(conn, NewNodeC);
    }

    [Fact]
    public async Task RestoredBeforeFailedStillStopsTheReconnectTimer()
    {
        using var data = new InProcessTestServer(log, PrimaryA);
        using var sentinel = new InProcessSentinelServer(data, ServiceName, PrimaryA, SentinelEndPoint, log);
        await using var conn = await ConnectionMultiplexer.ConnectAsync(CreateConfig(data, sentinel));
        var owner = conn.sentinelConnection!;
        var args = new ConnectionFailedEventArgs(null, conn, PrimaryA, ConnectionType.Interactive, ConnectionFailureType.SocketFailure, null, null);

        owner.OnManagedConnectionRestored(conn, args);
        await UntilAsync(() => conn.sentinelPrimaryReconnectTimer is null, "restore did not settle");
        int baseline = sentinel.PrimaryQueries;
        owner.OnManagedConnectionFailed(conn, args); // delayed worker delivery, socket is already healthy
        await UntilAsync(() => sentinel.PrimaryQueries > baseline && conn.sentinelPrimaryReconnectTimer is null, "late failure left a timer");
        Assert.Equal([PrimaryA], ConnectedPrimaries(conn));
    }

    [Fact]
    public async Task KnownHealthyPrimaryRetiresAReplicaNoLongerAdvertisedAndStopsRecovery()
    {
        using var data = new InProcessTestServer(log, PrimaryA);
        data.AddReplicaNode(ReplicaB, PrimaryA);
        using var sentinel = new InProcessSentinelServer(data, ServiceName, PrimaryA, SentinelEndPoint, log);
        await using var conn = await ConnectionMultiplexer.ConnectAsync(CreateConfig(data, sentinel));
        Assert.Contains(ReplicaB, conn.GetEndPoints());

        data.AddReplicaNode(NewNodeC, PrimaryA);
        RemoveAndDisconnect(data, ReplicaB);
        await UntilAsync(() => !conn.GetServer(ReplicaB).IsConnected, "removed replica stayed connected");
        var owner = conn.sentinelConnection!;
        var args = new ConnectionFailedEventArgs(null, conn, ReplicaB, ConnectionType.Interactive, ConnectionFailureType.SocketFailure, null, null);
        owner.OnManagedConnectionFailed(conn, args);
        await UntilAsync(() => !conn.GetEndPoints().Contains(ReplicaB) && conn.sentinelPrimaryReconnectTimer is null, "dead replica kept recovery alive");
        Assert.Equal([PrimaryA], ConnectedPrimaries(conn));
        Assert.DoesNotContain(ReplicaB, conn.GetServerSnapshot().ToArray().Select(x => x.EndPoint));
    }

    [Fact]
    public async Task HeldReplicaReportedDownIsNotRetired()
    {
        using var data = new InProcessTestServer(log, PrimaryA);
        data.AddReplicaNode(ReplicaB, PrimaryA);
        using var sentinel = new InProcessSentinelServer(data, ServiceName, PrimaryA, SentinelEndPoint, log);
        await using var conn = await ConnectionMultiplexer.ConnectAsync(CreateConfig(data, sentinel));
        Assert.Contains(ReplicaB, conn.GetEndPoints());

        // sentinel's momentary view (it is reconnecting to the node) against our live connection: the connection wins
        sentinel.ReplicaFlags[ReplicaB] = "s_down,disconnected";
        var owner = conn.sentinelConnection!;
        int changes = 0;
        conn.ConfigurationChanged += (_, _) => Interlocked.Increment(ref changes);
        Assert.False(await owner.SwitchPrimaryAsync(ReplicaB, conn));
        Assert.Contains(ReplicaB, conn.GetEndPoints());
        Assert.Contains(ReplicaB, conn.GetServerSnapshot().ToArray().Select(x => x.EndPoint));
        Assert.True(conn.GetServer(ReplicaB).IsConnected);
        Assert.Equal(0, Volatile.Read(ref changes));
        Assert.Null(conn.sentinelPrimaryReconnectTimer);
        Assert.Equal([PrimaryA], ConnectedPrimaries(conn));
    }

    [Fact]
    public async Task ReplicaReportedDownJoinsOnRecoveryOnceItsFlagsClear()
    {
        using var data = new InProcessTestServer(log, PrimaryA);
        using var sentinel = new InProcessSentinelServer(data, ServiceName, PrimaryA, SentinelEndPoint, log);
        await using var conn = await ConnectionMultiplexer.ConnectAsync(CreateConfig(data, sentinel));
        var owner = conn.sentinelConnection!;
        Assert.Equal([PrimaryA], conn.GetEndPoints());

        // a replica appears, but sentinel still holds it down: not a node to connect to yet
        data.AddReplicaNode(ReplicaB, PrimaryA);
        sentinel.ReplicaFlags[ReplicaB] = "s_down";
        Assert.False(await owner.SwitchPrimaryAsync(null, conn));
        Assert.Equal([PrimaryA], conn.GetEndPoints());
        Assert.Null(conn.sentinelPrimaryReconnectTimer);

        // The flags clear; the next recovery refresh adds the replica without retiring a held node.
        Assert.True(sentinel.ReplicaFlags.TryRemove(ReplicaB, out _));
        Assert.True(await owner.SwitchPrimaryAsync(null, conn));
        await UntilAsync(() => conn.GetEndPoints().Contains(ReplicaB) && conn.GetServer(ReplicaB).IsConnected, "cleared replica did not join");
        Assert.Equal([PrimaryA], ConnectedPrimaries(conn));
        Assert.Contains(PrimaryA, conn.GetServerSnapshot().ToArray().Select(x => x.EndPoint));
        await UntilAsync(() => conn.sentinelPrimaryReconnectTimer is null, "recovery left a timer");
    }

    [Fact]
    public async Task StaleSentinelNamingADownPrimaryIsPassedOver()
    {
        using var data = new InProcessTestServer(log, PrimaryA);
        data.AddReplicaNode(ReplicaB, PrimaryA);
        FailOverToNewNode(data);
        // the stale sentinel still names A, but holds it down; the other already knows C
        using var stale = new InProcessSentinelServer(data, ServiceName, PrimaryA, SentinelEndPoint, log) { PrimaryFlags = "master,s_down" };
        using var current = new InProcessSentinelServer(data, ServiceName, NewNodeC, SecondSentinelEndPoint, log);
        var config = CreateConfig(data, stale, current);
        config.EndPoints.Add(SecondSentinelEndPoint);
        await using var conn = await ConnectionMultiplexer.ConnectAsync(config);

        Assert.Equal([NewNodeC], ConnectedPrimaries(conn));
        Assert.DoesNotContain(PrimaryA, conn.GetServerSnapshot().ToArray().Where(x => x.IsConnected && !x.IsReplica).Select(x => x.EndPoint));
        Assert.True(stale.PrimaryQueries > 0, "the stale sentinel was never asked");
        await UntilAsync(() => conn.sentinelPrimaryReconnectTimer is null, "connect left a timer");
    }

    [Fact]
    public async Task LegacySentinelWithoutSentinelMasterStillResolvesThePrimary()
    {
        using var data = new InProcessTestServer(log, PrimaryA);
        data.AddReplicaNode(ReplicaB, PrimaryA);
        using var sentinel = new InProcessSentinelServer(data, ServiceName, PrimaryA, SentinelEndPoint, log) { SupportsSentinelMaster = false };
        await using var conn = await ConnectionMultiplexer.ConnectAsync(CreateConfig(data, sentinel));
        Assert.Equal([PrimaryA], ConnectedPrimaries(conn));
        Assert.Contains(ReplicaB, conn.GetEndPoints());

        FailOverToNewNode(data);
        sentinel.Primary = NewNodeC;
        await AssertPrimaryBecomesAsync(conn, NewNodeC);
    }

    [Fact]
    public async Task SentinelRoutesAreOnlyAddedAndDoNotNeedIdentity()
    {
        using var data = new InProcessTestServer(log, PrimaryA);
        var seed = new DnsEndPoint("sentinel-service", 26379);
        using var sentinel = new InProcessSentinelServer(data, ServiceName, PrimaryA, seed, log);
        sentinel.AddEmptyNode(SentinelEndPoint);
        sentinel.PeerFlags[SentinelEndPoint] = "s_down,disconnected";
        var config = CreateConfig(data, sentinel);
        config.EndPoints.Clear();
        config.EndPoints.Add(seed);
        await using var conn = await ConnectionMultiplexer.ConnectAsync(config);
        var owner = conn.sentinelConnection!;
        await UntilAsync(() => owner.GetEndPoints().Contains(SentinelEndPoint) && owner.GetServer(SentinelEndPoint).IsConnected, "down peer route was not added");
        Assert.Contains(seed, owner.GetEndPoints());

        // A later response no longer names the advertised IP. Both that route and the DNS seed must remain,
        // with one server per address.
        sentinel.PeerFlags.Clear();
        int baseline = sentinel.PeerQueries;
        var elapsed = Stopwatch.StartNew();
        while (sentinel.PeerQueries == baseline && elapsed.Elapsed < TimeSpan.FromSeconds(10))
        {
            await owner.UpdateSentinelAddressListAsync(ServiceName);
            await Task.Delay(50);
        }
        Assert.True(sentinel.PeerQueries > baseline, "empty peer response was not queried");
        Assert.Equal(2, owner.GetEndPoints().Length);
        Assert.Contains(seed, owner.GetEndPoints());
        Assert.Contains(SentinelEndPoint, owner.GetEndPoints());
        Assert.Single(owner.GetServerSnapshot().ToArray(), x => Equals(x.EndPoint, SentinelEndPoint));
    }

    [Fact]
    public async Task DemotionRecoveryUsesTheManagedServiceName()
    {
        using var data = new InProcessTestServer(log, PrimaryA);
        using var sentinel = new InProcessSentinelServer(data, ServiceName, PrimaryA, SentinelEndPoint, log);
        var otherPrimary = new IPEndPoint(IPAddress.Parse("192.0.2.4"), 6379);
        var otherNewPrimary = new IPEndPoint(IPAddress.Parse("192.0.2.5"), 6379);
        using var otherData = new InProcessTestServer(log, otherPrimary);
        using var otherSentinel = new InProcessSentinelServer(otherData, "othermaster", otherPrimary, SecondSentinelEndPoint, log);
        var config = CreateConfig(data, sentinel, otherData, otherSentinel);
        config.EndPoints.Add(SecondSentinelEndPoint);
        await using var conn = await ConnectionMultiplexer.ConnectAsync(config);
        var owner = conn.sentinelConnection!;
        var otherConfig = config.Clone();
        otherConfig.ServiceName = "othermaster";
        await using var otherConn = owner.GetSentinelMasterConnection(otherConfig);
        Assert.Equal([otherPrimary], ConnectedPrimaries(otherConn));

        // The first sentinel does not know this service. Its null answer must not hide the second service's
        // recovery, and the healthy first service must stay on its own primary.
        otherData.AddEmptyNode(otherNewPrimary);
        otherData.SetReplicaOf(otherPrimary, otherNewPrimary);
        otherSentinel.Primary = otherNewPrimary;
        await AssertPrimaryBecomesAsync(otherConn, otherNewPrimary);
        Assert.Equal([PrimaryA], ConnectedPrimaries(conn));
        await UntilAsync(() => otherConn.sentinelPrimaryReconnectTimer is null, "service recovery did not settle");
    }

    [Fact]
    public async Task SwitchRequestedDuringDiscoveryIsReplayed()
    {
        using var data = new InProcessTestServer(log, PrimaryA);
        data.AddReplicaNode(ReplicaB, PrimaryA);
        using var sentinel = new InProcessSentinelServer(data, ServiceName, PrimaryA, SentinelEndPoint, log);
        var config = CreateConfig(data, sentinel);
        config.ConfigCheckSeconds = 3600; // role polling must not rescue a lost announcement
        await using var conn = await ConnectionMultiplexer.ConnectAsync(config);
        var owner = conn.sentinelConnection!;
        var captured = NewSignal();
        var release = NewSignal();
        int replies = 0;
        sentinel.BeforePrimaryReplyAsync = () =>
        {
            if (Interlocked.Increment(ref replies) != 1) return Task.CompletedTask;
            captured.TrySetResult(true);
            return release.Task;
        };
        var oldView = owner.SwitchPrimaryAsync(null, conn);
        try
        {
            await AwaitSignalAsync(captured.Task);
            FailOverToNewNode(data);
            sentinel.Primary = NewNodeC;
            Assert.False(await owner.SwitchPrimaryAsync(PrimaryA, conn));
        }
        finally { release.TrySetResult(true); }
        await oldView;
        await AssertPrimaryBecomesAsync(conn, NewNodeC);
        await UntilAsync(() => conn.sentinelPrimaryReconnectTimer is null, "replayed switch did not settle");
        Assert.True(Volatile.Read(ref replies) >= 2);
    }

    [Fact]
    public async Task ReconnectWaitsOneSecondAfterTheAsyncAttemptCompletes()
    {
        using var data = new InProcessTestServer(log, PrimaryA);
        data.AddReplicaNode(ReplicaB, PrimaryA);
        using var sentinel = new InProcessSentinelServer(data, ServiceName, PrimaryA, SentinelEndPoint, log);
        await using var conn = await ConnectionMultiplexer.ConnectAsync(CreateConfig(data, sentinel));
        sentinel.Primary = ReplicaB; // still a replica, so recovery must retry
        var first = NewSignal();
        var release = NewSignal();
        var second = NewSignal();
        long secondAt = 0;
        int replies = 0;
        sentinel.BeforePrimaryReplyAsync = () =>
        {
            int number = Interlocked.Increment(ref replies);
            if (number == 1)
            {
                first.TrySetResult(true);
                return release.Task;
            }
            if (number == 2)
            {
                Interlocked.Exchange(ref secondAt, Stopwatch.GetTimestamp());
                second.TrySetResult(true);
            }
            return Task.CompletedTask;
        };
        var owner = conn.sentinelConnection!;
        owner.OnManagedConnectionFailed(conn, Failure(PrimaryA, conn));
        long releasedAt;
        try
        {
            await AwaitSignalAsync(first.Task);
            await Task.Delay(1200);
            Assert.Equal(1, Volatile.Read(ref replies)); // no second callback while the first attempt is blocked
            releasedAt = Stopwatch.GetTimestamp();
        }
        finally { release.TrySetResult(true); }
        await AwaitSignalAsync(second.Task);
        // Allow scheduling/clock granularity margin instead of adding nominal Task.Delay durations exactly.
        Assert.True((Interlocked.Read(ref secondAt) - releasedAt) / (double)Stopwatch.Frequency >= 0.8);
        sentinel.Primary = PrimaryA;
        await UntilAsync(() => conn.sentinelPrimaryReconnectTimer is null, "healthy primary did not stop recovery");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task IncompleteReplicaViewDoesNotRetireConnectedNodes(bool empty)
    {
        using var data = new InProcessTestServer(log, PrimaryA);
        data.AddReplicaNode(ReplicaB, PrimaryA);
        data.AddReplicaNode(NewNodeC, PrimaryA);
        using var sentinel = new InProcessSentinelServer(data, ServiceName, PrimaryA, SentinelEndPoint, log);
        await using var conn = await ConnectionMultiplexer.ConnectAsync(CreateConfig(data, sentinel));
        var held = conn.TryResolveServerEndPoint(ReplicaB);
        sentinel.ReplicaAddressesOverride = empty ? [] : [NewNodeC];
        await conn.sentinelConnection!.SwitchPrimaryAsync(null, conn);
        Assert.Contains(ReplicaB, conn.GetEndPoints());
        Assert.Same(held, conn.TryResolveServerEndPoint(ReplicaB));
        Assert.True(conn.GetServer(ReplicaB).IsConnected);
        Assert.Null(conn.sentinelPrimaryReconnectTimer);
    }

    [Fact]
    public async Task ReplicasComeFromTheSentinelWhosePrimaryWasAccepted()
    {
        using var data = new InProcessTestServer(log, PrimaryA);
        data.AddReplicaNode(ReplicaB, PrimaryA);
        using var accepted = new InProcessSentinelServer(data, ServiceName, PrimaryA, SentinelEndPoint, log);
        using var other = new InProcessSentinelServer(data, ServiceName, PrimaryA, SecondSentinelEndPoint, log)
        {
            PrimaryError = "ERR No such master with that name",
            ReplicaAddressesOverride = [],
        };
        var config = CreateConfig(data, accepted, other);
        config.EndPoints.Add(SecondSentinelEndPoint);
        await using var conn = await ConnectionMultiplexer.ConnectAsync(config);
        Assert.Contains(ReplicaB, conn.GetEndPoints());
        Assert.True(accepted.ReplicaQueries > 0);
        Assert.Equal(0, other.ReplicaQueries);
        Assert.Equal(0, other.LegacyQueries);
    }

    [Theory]
    [InlineData("LOADING Redis is loading the dataset in memory")]
    [InlineData("ERR No such master with that name")]
    public async Task SentinelErrorsDoNotUseTheFlaglessFallback(string error)
    {
        using var data = new InProcessTestServer(log, PrimaryA);
        data.AddReplicaNode(ReplicaB, PrimaryA);
        FailOverToNewNode(data);
        using var invalid = new InProcessSentinelServer(data, ServiceName, PrimaryA, SentinelEndPoint, log) { PrimaryError = error };
        using var current = new InProcessSentinelServer(data, ServiceName, NewNodeC, SecondSentinelEndPoint, log);
        var config = CreateConfig(data, invalid, current);
        config.EndPoints.Add(SecondSentinelEndPoint);
        await using var conn = await ConnectionMultiplexer.ConnectAsync(config);
        Assert.Equal([NewNodeC], ConnectedPrimaries(conn));
        Assert.Equal(0, invalid.LegacyQueries);
    }

    [Fact]
    public async Task DisconnectedHeldReplicaDoesNotKeepPrimaryRecoveryAlive()
    {
        using var data = new InProcessTestServer(log, PrimaryA);
        data.AddReplicaNode(ReplicaB, PrimaryA);
        using var sentinel = new InProcessSentinelServer(data, ServiceName, PrimaryA, SentinelEndPoint, log);
        await using var conn = await ConnectionMultiplexer.ConnectAsync(CreateConfig(data, sentinel));
        var held = conn.TryResolveServerEndPoint(ReplicaB);
        sentinel.ReplicaFlags[ReplicaB] = "s_down,disconnected";
        RemoveAndDisconnect(data, ReplicaB);
        await UntilAsync(() => !conn.GetServer(ReplicaB).IsConnected, "replica did not disconnect");
        var owner = conn.sentinelConnection!;
        owner.OnManagedConnectionFailed(conn, Failure(ReplicaB, conn));
        await UntilAsync(() => conn.sentinelPrimaryReconnectTimer is null, "dead held replica kept the timer");
        Assert.Contains(ReplicaB, conn.GetEndPoints());
        Assert.Same(held, conn.TryResolveServerEndPoint(ReplicaB));
        Assert.False(conn.GetServer(ReplicaB).IsConnected);
        int queries = sentinel.PrimaryQueries;
        await Task.Delay(2500);
        Assert.Equal(queries, sentinel.PrimaryQueries);
        Assert.Null(conn.sentinelPrimaryReconnectTimer);
        await conn.GetDatabase().StringSetAsync("primary-still-works", "yes");
    }

    [Fact]
    public async Task SentinelAnnouncementUsesTheServiceAfterTheSeparator()
    {
        using var data = new InProcessTestServer(log, PrimaryA);
        using var sentinel = new InProcessSentinelServer(data, ServiceName, PrimaryA, SentinelEndPoint, log);
        await using var conn = await ConnectionMultiplexer.ConnectAsync(CreateConfig(data, sentinel));
        sentinel.AddEmptyNode(SecondSentinelEndPoint);
        sentinel.PeerFlags[SecondSentinelEndPoint] = "";
        Assert.True(sentinel.ForAllClients(client => client.Publish(RedisChannel.Literal("+sentinel"),
            "sentinel peer 192.0.2.11 26379 @ mymaster 192.0.2.1 6379")) > 0);
        var owner = conn.sentinelConnection!;
        await UntilAsync(() => owner.GetEndPoints().Contains(SecondSentinelEndPoint), "announcement did not discover the peer");
    }

    [Fact]
    public async Task SentinelAddressRequestDuringAnOlderReplyIsReplayed()
    {
        using var data = new InProcessTestServer(log, PrimaryA);
        using var sentinel = new InProcessSentinelServer(data, ServiceName, PrimaryA, SentinelEndPoint, log);
        await using var conn = await ConnectionMultiplexer.ConnectAsync(CreateConfig(data, sentinel));
        var owner = conn.sentinelConnection!;
        var captured = NewSignal();
        var release = NewSignal();
        int replies = 0;
        sentinel.BeforePeerReplyAsync = () =>
        {
            if (Interlocked.Increment(ref replies) != 1) return Task.CompletedTask;
            captured.TrySetResult(true);
            return release.Task;
        };
        var update = owner.UpdateSentinelAddressListAsync(ServiceName);
        try
        {
            await AwaitSignalAsync(captured.Task);
            sentinel.AddEmptyNode(SecondSentinelEndPoint);
            sentinel.PeerFlags[SecondSentinelEndPoint] = "";
            await owner.UpdateSentinelAddressListAsync(ServiceName);
        }
        finally { release.TrySetResult(true); }
        await update;
        await UntilAsync(() => owner.GetEndPoints().Contains(SecondSentinelEndPoint), "pending peer refresh was lost");
        Assert.True(Volatile.Read(ref replies) >= 2);
    }

    [Fact]
    public async Task SentinelAddressIsRetainedWhileReconfigureIsBusy()
    {
        using var data = new InProcessTestServer(log, PrimaryA);
        using var sentinel = new InProcessSentinelServer(data, ServiceName, PrimaryA, SentinelEndPoint, log);
        await using var conn = await ConnectionMultiplexer.ConnectAsync(CreateConfig(data, sentinel));
        var owner = conn.sentinelConnection!;
        sentinel.AddEmptyNode(SecondSentinelEndPoint);
        sentinel.PeerFlags[SecondSentinelEndPoint] = "";
        await UntilAsync(() => owner.TryClaimReconfigure("unit holds the claim"), "could not claim reconfigure");
        Task update;
        try
        {
            update = owner.UpdateSentinelAddressListAsync(ServiceName);
            await UntilAsync(() => owner.EndPoints.Contains(SecondSentinelEndPoint), "busy claim lost the address");
        }
        finally { owner.ReleaseReconfigureClaim(); }
        await update;
        await UntilAsync(() => owner.GetServer(SecondSentinelEndPoint).IsConnected, "pending reconfigure did not connect the peer");
    }

    [Fact]
    public async Task MissingServiceNameDoesNotRearmAnEmptyTimer()
    {
        using var data = new InProcessTestServer(log, PrimaryA);
        using var sentinel = new InProcessSentinelServer(data, ServiceName, PrimaryA, SentinelEndPoint, log);
        var config = CreateConfig(data, sentinel);
        await using var conn = await ConnectionMultiplexer.ConnectAsync(config);
        config.ServiceName = null;
        var owner = conn.sentinelConnection!;
        int queries = sentinel.PrimaryQueries;
        owner.OnManagedConnectionFailed(conn, Failure(PrimaryA, conn));
        Assert.False(await owner.SwitchPrimaryAsync(null, conn));
        Assert.Null(conn.sentinelPrimaryReconnectTimer);
        Assert.Equal(queries, sentinel.PrimaryQueries);
    }

    [Fact]
    public async Task AllSentinelsUnavailableKeepRecoveryUntilOneReturns()
    {
        using var data = new InProcessTestServer(log, PrimaryA);
        using var sentinel = new InProcessSentinelServer(data, ServiceName, PrimaryA, SentinelEndPoint, log);
        var config = CreateConfig(data, sentinel);
        config.AsyncTimeout = 250;
        await using var conn = await ConnectionMultiplexer.ConnectAsync(config);
        var owner = conn.sentinelConnection!;
        RemoveAndDisconnect(sentinel, SentinelEndPoint);
        owner.OnManagedConnectionFailed(conn, Failure(PrimaryA, conn));
        await Task.Delay(2500);
        Assert.NotNull(conn.sentinelPrimaryReconnectTimer);
        sentinel.AddEmptyNode(SentinelEndPoint);
        await UntilAsync(() => conn.sentinelPrimaryReconnectTimer is null && owner.IsConnected, "sentinel return did not end recovery");
    }

    [Fact]
    public async Task DisposeDuringATimerAttemptDoesNotRestartRecovery()
    {
        using var data = new InProcessTestServer(log, PrimaryA);
        using var sentinel = new InProcessSentinelServer(data, ServiceName, PrimaryA, SentinelEndPoint, log);
        var conn = await ConnectionMultiplexer.ConnectAsync(CreateConfig(data, sentinel));
        await using (conn)
        {
            var captured = NewSignal();
            var release = NewSignal();
            sentinel.BeforePrimaryReplyAsync = () =>
            {
                captured.TrySetResult(true);
                return release.Task;
            };
            conn.sentinelConnection!.OnManagedConnectionFailed(conn, Failure(PrimaryA, conn));
            Task dispose;
            try
            {
                await AwaitSignalAsync(captured.Task);
                dispose = conn.DisposeAsync().AsTask();
            }
            finally { release.TrySetResult(true); }
            await dispose;
            await Task.Delay(1200);
            Assert.True(conn.IsDisposed);
            Assert.Null(conn.sentinelPrimaryReconnectTimer);
        }
    }

    [Fact]
    public async Task FailedInfoWriteDoesNotCountAsAReplicationCheck()
    {
        using var data = new InProcessTestServer(log, PrimaryA);
        var config = data.GetClientConfig();
        config.Tunnel = InProcessSentinelServer.CreateTunnel(data);
        config.BacklogPolicy = BacklogPolicy.FailFast;
        config.ConfigCheckSeconds = 1;
        await using var conn = await ConnectionMultiplexer.ConnectAsync(config);
        var server = conn.TryResolveServerEndPoint(PrimaryA)!;
        RemoveAndDisconnect(data, PrimaryA);
        await UntilAsync(() => !server.IsConnected, "server did not disconnect");
        Assert.False(await server.CheckInfoReplicationAsync());
    }

    [Fact]
    public async Task ConcurrentManagedConnectReturnsOneChildAndDisposesTheOther()
    {
        using var data = new InProcessTestServer(log, PrimaryA);
        using var sentinel = new InProcessSentinelServer(data, ServiceName, PrimaryA, SentinelEndPoint, log);
        var ready = NewSignal();
        var release = NewSignal();
        var config = CreateConfig(data, sentinel);
        config.Protocol = RedisProtocol.Resp2;
        int connecting = 0;
        config.Tunnel = new BlockedDataTunnel(config.Tunnel!, PrimaryA, release.Task, () =>
        {
            if (Interlocked.Increment(ref connecting) == 2) ready.TrySetResult(true);
        });
        await using var owner = await ConnectionMultiplexer.SentinelConnectAsync(config);
        Task<ConnectionMultiplexer> Start() => Task.Factory.StartNew(() => owner.GetSentinelMasterConnection(config),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        var first = Start();
        var second = Start();
        try { await AwaitSignalAsync(ready.Task); }
        finally { release.TrySetResult(true); }
        await using var child = await first;
        Assert.Same(child, await second);
        Assert.Single(owner.sentinelConnectionChildren);
        await UntilAsync(() => data.ClientCount == 2, "losing managed connection was not disposed");
    }

    [Fact]
    public async Task SynchronousSoftConnectReturnsBeforeAsyncRoleTimeoutAndRecovers()
    {
        using var data = new InProcessTestServer(log, PrimaryA);
        using var sentinel = new InProcessSentinelServer(data, ServiceName, PrimaryA, SentinelEndPoint, log);
        var release = NewSignal();
        var config = CreateConfig(data, sentinel);
        config.AbortOnConnectFail = false;
        config.ConnectTimeout = 200;
        config.ConnectRetry = 1;
        config.SyncTimeout = 500;
        config.AsyncTimeout = 5000;
        config.Tunnel = new BlockedDataTunnel(config.Tunnel!, PrimaryA, release.Task);
        var connecting = Task.Factory.StartNew(() => ConnectionMultiplexer.Connect(config), CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);
        ConnectionMultiplexer conn;
        try
        {
            Assert.Same(connecting, await Task.WhenAny(connecting, Task.Delay(2500)));
            conn = await connecting;
            Assert.False(conn.IsConnected);
        }
        finally { release.TrySetResult(true); }
        await using (conn)
        {
            await AssertPrimaryBecomesAsync(conn, PrimaryA);
            await UntilAsync(() => conn.sentinelPrimaryReconnectTimer is null, "soft connect recovery did not settle");
        }
    }

    private sealed class BlockedDataTunnel(Tunnel inner, EndPoint endpoint, Task release, Action? connecting = null) : Tunnel
    {
        public override ValueTask<EndPoint?> GetSocketConnectEndpointAsync(EndPoint target, CancellationToken cancellationToken)
            => inner.GetSocketConnectEndpointAsync(target, cancellationToken);

        public override ValueTask<Stream?> BeforeAuthenticateAsync(EndPoint target, ConnectionType connectionType, Socket? socket, CancellationToken cancellationToken)
            => Equals(target, endpoint) ? new(AcceptAsync(target, connectionType, socket, cancellationToken))
                : inner.BeforeAuthenticateAsync(target, connectionType, socket, cancellationToken);

        private async Task<Stream?> AcceptAsync(EndPoint target, ConnectionType connectionType, Socket? socket, CancellationToken cancellationToken)
        {
            if (connectionType == ConnectionType.Interactive) connecting?.Invoke();
            await release;
            return await inner.BeforeAuthenticateAsync(target, connectionType, socket, cancellationToken);
        }
    }

    private static void RemoveAndDisconnect(InProcessTestServer server, EndPoint endpoint)
    {
        Assert.True(server.TryGetNode(endpoint, out var node));
        node.UpdateSlots([]);
        Assert.True(server.RemoveNode(endpoint));
        server.ForAllClients(client =>
        {
            if (ReferenceEquals(client.Node, node)) client.Dispose();
        });
    }

    private static TaskCompletionSource<bool> NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task AwaitSignalAsync(Task signal)
        => Assert.Same(signal, await Task.WhenAny(signal, Task.Delay(10000)));

    private static ConnectionFailedEventArgs Failure(EndPoint endpoint, ConnectionMultiplexer conn)
        => new(null, conn, endpoint, ConnectionType.Interactive, ConnectionFailureType.SocketFailure, null, null);

    private static ConfigurationOptions CreateConfig(InProcessTestServer data, InProcessSentinelServer sentinel, params InProcessTestServer[] more)
    {
        var config = data.GetClientConfig();
        config.EndPoints.Clear();
        config.EndPoints.Add(SentinelEndPoint);
        config.ServiceName = ServiceName;
        config.Tunnel = InProcessSentinelServer.CreateTunnel([data, sentinel, .. more]);
        config.ConfigCheckSeconds = 1; // how soon the demoted node's role is re-read
        return config;
    }

    // C is promoted, and A and B follow it; nothing is announced and no connection drops
    private static void FailOverToNewNode(InProcessTestServer data)
    {
        data.AddEmptyNode(NewNodeC);
        data.SetReplicaOf(PrimaryA, NewNodeC);
        data.SetReplicaOf(ReplicaB, NewNodeC);
    }

    private async Task AssertPrimaryBecomesAsync(IConnectionMultiplexer conn, EndPoint expected)
    {
        var timer = Stopwatch.StartNew();
        await UntilAsync(() => ConnectedPrimaries(conn).SequenceEqual([expected]), $"primary did not become {Format.ToString(expected)}");
        log.WriteLine($"primary is {Format.ToString(expected)} after {timer.ElapsedMilliseconds}ms");

        Assert.Equal([expected], ConnectedPrimaries(conn));
        Assert.Equal(expected, conn.GetDatabase().IdentifyEndpoint("demotion-key", CommandFlags.DemandMaster));
    }

    private static async Task UntilAsync(Func<bool> condition, string failure)
    {
        var timer = Stopwatch.StartNew();
        while (!condition())
        {
            if (timer.Elapsed > TimeSpan.FromSeconds(20)) Assert.Fail($"{failure} within {timer.Elapsed.TotalSeconds:0}s");
            await Task.Delay(100);
        }
    }

    private static EndPoint[] ConnectedPrimaries(IConnectionMultiplexer conn)
        => conn.GetServers().Where(static s => s.IsConnected && !s.IsReplica).Select(static s => s.EndPoint).ToArray();
}
