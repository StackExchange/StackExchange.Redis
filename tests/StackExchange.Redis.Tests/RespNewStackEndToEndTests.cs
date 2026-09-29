using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RESPite.Operations;
using RESPite.Transports;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// The new stack against a REAL server, with none of the old pipeline in the path.
/// </summary>
/// <remarks>
/// <para>
/// Everything else that talks to a server goes through <c>RespMessageExecutor</c>, which wraps the
/// rendered frame in a <c>Message</c>. This path is: interpolated writer → context →
/// <c>RespConnectionExecutor</c> → <c>RespConnection</c> → <c>StreamDuplexTransport</c> → socket. No
/// <c>Message</c>, no <c>ResultProcessor</c>, no result box.
/// </para>
/// <para>
/// <b>What is deliberately absent, and why these tests are still honest:</b> no handshake, no
/// <c>SELECT</c>, no reconnect, no backlog. A plain TCP connection to a RESP server speaks RESP2 on
/// database 0 without being asked, which is enough to prove the path carries real bytes both ways. It is
/// <i>not</i> enough to benchmark against - see the queue.
/// </para>
/// </remarks>
public class RespNewStackEndToEndTests(ITestOutputHelper output)
{
    /// <summary>A key unique to the calling test, so these can run alongside each other.</summary>
    /// <remarks>
    /// This class does not derive from <c>TestBase</c> - it builds its own connection rather than taking
    /// one from the fixture, which is the whole point - so it needs its own version of <c>Me()</c>.
    /// </remarks>
    private static RedisKey Me([System.Runtime.CompilerServices.CallerMemberName] string caller = "")
        => $"{nameof(RespNewStackEndToEndTests)}:{caller}";

    private static async Task<(RespDatabaseContext Context, RespConnection Connection, StreamDuplexTransport Transport)> ConnectAsync(
        string? host = null, int port = 0)
    {
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        await socket.ConnectAsync(host ?? TestConfig.Current.PrimaryServer, port == 0 ? TestConfig.Current.PrimaryPort : port);

        var transport = new StreamDuplexTransport(new NetworkStream(socket, ownsSocket: true));
        var connection = new RespClientConnection(transport, static (in RespRedirect _, RespPayloadOperation _) => false);
        var context = new RespContext().WithExecutor(new RespConnectionExecutor(connection, 0));
        return (new RespDatabaseContext(context), connection, transport);
    }

    [Fact]
    public async Task TheHandshakeNegotiatesRESP3AgainstARealServer()
    {
        RespDatabaseContext context;
        StreamDuplexTransport transport;
        try
        {
            (context, _, transport) = await ConnectAsync();
        }
        catch (SocketException ex)
        {
            Assert.Skip($"Unable to connect to server: {ex.Message}");
            return;
        }

        await using var owner = transport;

        var result = await RespHandshake.PerformAsync(context, clientName: "resp-new-stack", database: 0);
        var protocol = result.Protocol;

        // the test servers are modern, so this is RESP3 - and reading it from the reply rather than
        // assuming it is the entire point of awaiting HELLO
        Assert.Equal(RedisProtocol.Resp3, protocol);

        // and the connection still works afterwards, which is what a handshake is for
        RedisKey key = Me();
        await context.Keys.DeleteAsync(key);
        Assert.Equal(1, (long)await context.Strings.IncrementAsync(key));
    }

    [Fact]
    public async Task TheHandshakeReportsWhatTheServerIsAndSetsTheTopology()
    {
        // this is what turns the ordering invariant from a rule somebody has to remember into something
        // structural: the topology is set BEFORE the handshake returns, which is before the endpoint
        // executor publishes the connection and drains its backlog
        RespDatabaseContext context;
        StreamDuplexTransport transport;
        try
        {
            (context, _, transport) = await ConnectAsync();
        }
        catch (SocketException ex)
        {
            Assert.Skip($"Unable to connect to server: {ex.Message}");
            return;
        }

        await using var owner = transport;

        var topology = new RespTopology();
        Assert.Equal(RespClusterState.Unknown, topology.State);

        var result = await RespHandshake.PerformAsync(context, topology: topology);

        // the standalone test server: HELLO says mode=standalone, so no CLUSTER INFO was needed
        Assert.Equal(ServerType.Standalone, result.ServerType);
        Assert.Equal(RespClusterState.No, topology.State);
        Assert.False(topology.NeedsSlots); // and hashing stops, for the life of the connection
    }

    [Fact]
    public async Task AgainstARealClusterTheHandshakeSaysSoAndSlotsStartMattering()
    {
        // the half that actually matters, and the one a standalone server cannot prove: HELLO reports
        // mode=cluster, the topology latches to Yes, and hashing switches on for every subsequent key
        RespDatabaseContext context;
        StreamDuplexTransport transport;
        try
        {
            (context, _, transport) = await ConnectAsync(TestConfig.Current.ClusterServer, TestConfig.Current.ClusterStartPort);
        }
        catch (SocketException ex)
        {
            Assert.Skip($"Unable to connect to cluster server: {ex.Message}");
            return;
        }

        await using var owner = transport;

        var topology = new RespTopology();
        var clustered = new RespDatabaseContext(context.Raw.WithTopology(topology));

        // before: nothing known, so slots are computed speculatively but not obeyed
        Assert.True(topology.NeedsSlots);
        Assert.False(topology.RoutesBySlot);

        var result = await RespHandshake.PerformAsync(clustered, topology: topology);

        Assert.Equal(ServerType.Cluster, result.ServerType);
        Assert.Equal(RespClusterState.Yes, topology.State);
        Assert.True(topology.RoutesBySlot);   // and now the slot decides

        using var request = clustered.Raw.Render($"{RedisCommand.GET}{(RedisKey)"user:1"}").Detach();
        Assert.Equal(ServerSelectionStrategy.GetHashSlot((RedisKey)"user:1"), request.Slot);

        output.WriteLine($"cluster node reported {result.ServerType} over {result.Protocol}");
    }

    [Fact]
    public async Task ARealClusterNodeRedirectsInAFormWeCanRead()
    {
        // the parser's unit tests assert what I BELIEVE the format is; this asserts what a server
        // actually sends. Worth having separately, because a format assumption that is wrong passes
        // every test written from the same assumption.
        RespDatabaseContext context;
        StreamDuplexTransport transport;
        try
        {
            (context, _, transport) = await ConnectAsync(TestConfig.Current.ClusterServer, TestConfig.Current.ClusterStartPort);
        }
        catch (SocketException ex)
        {
            Assert.Skip($"Unable to connect to cluster server: {ex.Message}");
            return;
        }

        await using var owner = transport;

        // hunt for a key this node does not own; with 16,384 slots over a handful of nodes it takes very
        // few attempts, but the loop makes the test independent of which node answered
        RespRedirect redirect = default;
        var found = false;
        for (var i = 0; i < 200 && !found; i++)
        {
            try
            {
                await context.Strings.GetAsync($"{nameof(ARealClusterNodeRedirectsInAFormWeCanRead)}:{i}");
            }
            catch (RedisServerException ex) when (ex.Message.StartsWith("MOVED", StringComparison.Ordinal))
            {
                found = RespRedirect.TryParseText(Encoding.UTF8.GetBytes(ex.Message), out redirect);
                Assert.True(found, $"failed to read a redirect the server sent: '{ex.Message}'");
            }
        }

        if (!found)
        {
            Assert.Skip("this node owns every slot we tried; nothing to redirect");
            return;
        }

        Assert.True(redirect.IsMoved);
        Assert.InRange(redirect.Slot, 0, 16383);
        Assert.False(redirect.IsUnroutable);
        Assert.NotNull(redirect.Endpoint);
        output.WriteLine($"server redirected slot {redirect.Slot} to {redirect.Endpoint}");
    }

    [Fact]
    public async Task TheHandshakeFallsBackToClusterInfoWithoutHello()
    {
        // a RESP2 server, or one with no HELLO at all, still has to yield an answer - CLUSTER INFO is
        // unambiguous and works there. A server where CLUSTER is unavailable is, by that fact, not one.
        RespDatabaseContext context;
        StreamDuplexTransport transport;
        try
        {
            (context, _, transport) = await ConnectAsync();
        }
        catch (SocketException ex)
        {
            Assert.Skip($"Unable to connect to server: {ex.Message}");
            return;
        }

        await using var owner = transport;

        var topology = new RespTopology();
        var result = await RespHandshake.PerformAsync(context, preferResp3: false, topology: topology);

        Assert.Equal(RedisProtocol.Resp2, result.Protocol);
        Assert.Equal(ServerType.Standalone, result.ServerType);
        Assert.Equal(RespClusterState.No, topology.State);
    }

    [Fact]
    public async Task TheHandshakeCanDeclineRESP3AndStillWork()
    {
        RespDatabaseContext context;
        StreamDuplexTransport transport;
        try
        {
            (context, _, transport) = await ConnectAsync();
        }
        catch (SocketException ex)
        {
            Assert.Skip($"Unable to connect to server: {ex.Message}");
            return;
        }

        await using var owner = transport;

        var protocol = (await RespHandshake.PerformAsync(context, preferResp3: false)).Protocol;

        Assert.Equal(RedisProtocol.Resp2, protocol); // never asked, so never got it
        RedisKey key = Me();
        await context.Keys.DeleteAsync(key);
        Assert.Equal(1, (long)await context.Strings.IncrementAsync(key));
    }

    [Fact]
    public async Task TheHandshakeSelectsADatabase()
    {
        RespDatabaseContext context;
        StreamDuplexTransport transport;
        try
        {
            (context, _, transport) = await ConnectAsync();
        }
        catch (SocketException ex)
        {
            Assert.Skip($"Unable to connect to server: {ex.Message}");
            return;
        }

        await using var owner = transport;

        RedisKey key = Me();
        await RespHandshake.PerformAsync(context, database: 3);
        await context.Keys.DeleteAsync(key);
        await context.Strings.SetAsync(key, "in-three");

        // prove it really moved: the same key on database 0, over a separate connection, is absent
        var (other, _, otherTransport) = await ConnectAsync();
        await using var otherOwner = otherTransport;
        Assert.True((await other.Strings.GetAsync(key)).IsNull);

        Assert.Equal("in-three", (string?)await context.Strings.GetAsync(key));
    }

    [Fact]
    public async Task TheHandshakeAuthenticates()
    {
        RespDatabaseContext context;
        StreamDuplexTransport transport;
        try
        {
            (context, _, transport) = await ConnectAsync(TestConfig.Current.SecureServer, TestConfig.Current.SecurePort);
        }
        catch (SocketException ex)
        {
            Assert.Skip($"Unable to connect to secure server: {ex.Message}");
            return;
        }

        await using var owner = transport;

        RedisKey key = Me();
        var protocol = (await RespHandshake.PerformAsync(context, password: TestConfig.Current.SecurePassword)).Protocol;
        Assert.Equal(RedisProtocol.Resp3, protocol);

        // AUTH goes FIRST, deliberately: an authenticated server answers HELLO with NOAUTH, so asking
        // for RESP3 before authenticating would decline the protocol for the wrong reason
        await context.Keys.DeleteAsync(key);
        Assert.Equal(1, (long)await context.Strings.IncrementAsync(key));
    }

    [Fact]
    public async Task AnUnauthenticatedConnectionToASecureServerIsRefused()
    {
        RespDatabaseContext context;
        StreamDuplexTransport transport;
        try
        {
            (context, _, transport) = await ConnectAsync(TestConfig.Current.SecureServer, TestConfig.Current.SecurePort);
        }
        catch (SocketException ex)
        {
            Assert.Skip($"Unable to connect to secure server: {ex.Message}");
            return;
        }

        await using var owner = transport;

        // the control for the test above: without the password, the server really does refuse
        var ex2 = await Assert.ThrowsAsync<RedisServerException>(async () => await context.Strings.GetAsync(Me()));
        Assert.StartsWith("NOAUTH", ex2.Message);
    }

    [Fact]
    public async Task TheEndpointExecutorReconnectsAfterARealSocketDies()
    {
        // the fake-transport tests prove the state machine; this proves it against a socket that really
        // goes away, with a real handshake on the replacement connection
        var sockets = new List<Socket>();
        var executor = new RespEndpointExecutor(
            async ct =>
            {
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                await socket.ConnectAsync(TestConfig.Current.PrimaryServer, TestConfig.Current.PrimaryPort);
                lock (sockets) sockets.Add(socket);

                var connection = new RespClientConnection(new StreamDuplexTransport(new NetworkStream(socket, ownsSocket: true)), static (in RespRedirect _, RespPayloadOperation _) => false);

                // the replacement is handshaked too, which is the point: a reconnected connection that
                // skipped SELECT would quietly be on the wrong database
                var handshakeContext = new RespDatabaseContext(
                    new RespContext().WithExecutor(new RespConnectionExecutor(connection, 0)));
                await RespHandshake.PerformAsync(handshakeContext, database: 0, cancellationToken: ct);
                return connection;
            });

        await using var owner = executor;
        var context = new RespDatabaseContext(new RespContext().WithExecutor(executor));

        RedisKey key = Me();
        try
        {
            await context.Keys.DeleteAsync(key);
        }
        catch (SocketException ex)
        {
            Assert.Skip($"Unable to connect to server: {ex.Message}");
            return;
        }

        Assert.Equal(1, (long)await context.Strings.IncrementAsync(key));
        Assert.Equal(1, executor.Connects);

        // kill it the way a network does: abortive close, no FIN
        lock (sockets) sockets[sockets.Count - 1].Close(timeout: 0);

        // A command issued in the window between the socket dying and the read loop noticing reaches the
        // dead connection and fails. That is CORRECT and must not be smoothed over here: the bytes went
        // to a socket, so whether the server applied them is unknown, and quietly re-sending an INCR
        // would double it. Deciding to retry is the retry layer's job, with the flags and the category.
        var casualties = 0;
        long observed = 0;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                observed = (long)await context.Strings.IncrementAsync(key);
                break;
            }
            catch (Exception ex) when (ex is IOException or RedisConnectionException or SocketException)
            {
                casualties++;
            }
        }

        // recovered: a new connection, freshly handshaked, and the counter moved on from 1
        Assert.True(observed > 1, $"expected the counter to advance; saw {observed}");
        Assert.Equal(2, executor.Connects);
        Assert.True(executor.IsConnectedNow);

        output.WriteLine($"recovered across {executor.Connects} connections, {casualties} command(s) lost to the dead socket");
    }

    [Fact]
    public async Task ACancellationTokenIsHonouredAgainstARealServer()
    {
        // the most-requested missing feature, and the whole reason the operation holds its own
        // registration: the token competes for the same single-winner outcome claim a reply does
        RespDatabaseContext context;
        StreamDuplexTransport transport;
        try
        {
            (context, _, transport) = await ConnectAsync();
        }
        catch (SocketException ex)
        {
            Assert.Skip($"Unable to connect to server: {ex.Message}");
            return;
        }

        await using var owner = transport;

        // a token cancelled BEFORE we start is refused without sending anything, whatever the executor
        using (var precancelled = new CancellationTokenSource())
        {
            precancelled.Cancel(); // not CancelAsync: this project targets net481 too
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await context.Strings.GetAsync(Me(), cancellationToken: precancelled.Token));
        }

        // and a live token is now ACCEPTED rather than refused with NotImplementedException, which is
        // what SER310 has been telling people to migrate for
        await context.Keys.DeleteAsync(Me());
        using var cts = new CancellationTokenSource();
        Assert.Equal(1, (long)await context.Strings.IncrementAsync(Me(), cancellationToken: cts.Token));

        // the registration is released on a definite outcome, so cancelling afterwards changes nothing
        cts.Cancel();
        Assert.Equal("1", (string?)await context.Strings.GetAsync(Me()));
    }

    [Fact]
    public async Task TheMessageShimStillRefusesACancellableToken()
    {
        // the capability is per-executor, and the shim's answer is still no - honestly so, because the
        // classic pipeline cannot withdraw a request that has reached the socket
        await using var conn = ConnectionMultiplexer.Connect(TestConfig.Current.PrimaryServerAndPort);
        var context = conn.GetDatabase().Context;

        // ...and under the engine flag the database is NOT the shim, so there is no shim here to refuse
        // anything. Skipped rather than relaxed: the claim is about what the Message pipeline can promise,
        // and it stays true where that pipeline is what runs the command.
        if (context.Raw.Executor is not RespMessageExecutor)
        {
            Assert.Skip("requires the Message shim; with the new core as the engine, this context can cancel");
        }

        using var cts = new CancellationTokenSource();
        await Assert.ThrowsAsync<NotImplementedException>(
            async () => await context.Strings.GetAsync(Me(), cancellationToken: cts.Token));
    }

    [Fact]
    public async Task ATransactionRunsAtomicallyAgainstARealServer()
    {
        // the fake-transport tests prove the shape against replies I wrote; this proves it against the
        // ones a server actually sends - which is where an assumption about +QUEUED or EXEC would show
        RespDatabaseContext context;
        RespConnection connection;
        StreamDuplexTransport transport;
        try
        {
            (context, connection, transport) = await ConnectAsync();
        }
        catch (SocketException ex)
        {
            Assert.Skip($"Unable to connect to server: {ex.Message}");
            return;
        }

        await using var owner = transport;

        RedisKey counter = $"{Me()}:counter";
        RedisKey list = $"{Me()}:list";
        await context.Keys.DeleteAsync(counter);
        await context.Keys.DeleteAsync(list);

        var endpoint = new RespConnectionExecutor(connection, 0);
        var tran = new RespTransactionExecutor(endpoint);
        var queued = new RespDatabaseContext(new RespContext().WithExecutor(tran));

        // a scalar and an aggregate, so the element walk is exercised on a real reply
        var incr = queued.Strings.IncrementAsync(counter);
        var push = queued.Lists.RightPushAsync(list, "x");
        var range = queued.Lists.RangeAsync(list, 0, -1);

        Assert.True(await tran.ExecuteAsync());

        Assert.Equal(1, await incr);
        Assert.Equal(1, await push);
        using var values = await range;
        Assert.Equal(1, values.Length);
        Assert.Equal("x", (string?)values.Span[0]);

        // and the connection is usable afterwards: MULTI state was left cleanly
        Assert.Equal(2, (long)await context.Strings.IncrementAsync(counter));
    }

    [Fact]
    public async Task AConditionalTransactionRunsOnlyWhenItsConditionHolds()
    {
        RespDatabaseContext context;
        RespConnection connection;
        StreamDuplexTransport transport;
        try
        {
            (context, connection, transport) = await ConnectAsync();
        }
        catch (SocketException ex)
        {
            Assert.Skip($"Unable to connect to server: {ex.Message}");
            return;
        }

        await using var owner = transport;

        RedisKey guard = $"{Me()}:guard";
        RedisKey counter = $"{Me()}:counter";
        await context.Keys.DeleteAsync(guard);
        await context.Keys.DeleteAsync(counter);

        var endpoint = new RespConnectionExecutor(connection, 0);

        // the guard does NOT exist, so the transaction must not be sent at all
        var blocked = new RespTransactionExecutor(endpoint, new RespContext());
        blocked.AddCondition(Condition.KeyExists(guard));
        var blockedContext = new RespDatabaseContext(new RespContext().WithExecutor(blocked));
        var notRun = blockedContext.Strings.IncrementAsync(counter);

        Assert.False(await blocked.ExecuteAsync());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await notRun);
        Assert.False(await context.Keys.ExistsAsync(counter)); // and nothing touched the server

        // now the guard exists, so the same shape goes through
        await context.Strings.SetAsync(guard, "yes");

        var allowed = new RespTransactionExecutor(endpoint, new RespContext());
        allowed.AddCondition(Condition.KeyExists(guard));
        var allowedContext = new RespDatabaseContext(new RespContext().WithExecutor(allowed));
        var ran = allowedContext.Strings.IncrementAsync(counter);

        Assert.True(await allowed.ExecuteAsync());
        Assert.Equal(1, await ran);

        // and the connection is left clean: no dangling WATCH, no open MULTI
        Assert.Equal(2, (long)await context.Strings.IncrementAsync(counter));
    }

    [Fact]
    public async Task AContendedWatchNeverAppliesHalfOfATransaction()
    {
        // DELIBERATELY a race, and it asserts the invariant rather than the winner: the interfering write
        // may land before or after the check, and both are legitimate. What must hold either way is that
        // "reported false" and "did not apply" agree - the outcome a torn transaction would break.
        //
        // The abort branch ITSELF is pinned deterministically by the unit test over a fake transport,
        // which can simply reply null to EXEC. This one exists because only a real server decides when
        // a watch trips, and a shape that passes against a fake can still hang or mis-read against one.
        RespDatabaseContext context;
        RespConnection connection;
        StreamDuplexTransport transport;
        try
        {
            (context, connection, transport) = await ConnectAsync();
        }
        catch (SocketException ex)
        {
            Assert.Skip($"Unable to connect to server: {ex.Message}");
            return;
        }

        await using var owner = transport;

        RedisKey guard = $"{Me()}:guard";
        RedisKey counter = $"{Me()}:counter";
        await context.Keys.DeleteAsync(counter);
        await context.Strings.SetAsync(guard, "before");

        var endpoint = new RespConnectionExecutor(connection, 0);
        var tran = new RespTransactionExecutor(endpoint, new RespContext());
        tran.AddCondition(Condition.StringEqual(guard, "before"));
        var queued = new RespDatabaseContext(new RespContext().WithExecutor(tran));
        var pending = queued.Strings.IncrementAsync(counter);

        // ExecuteAsync runs the check first; interfering needs a SECOND connection, because changing the
        // key from this one would not trip our own watch
        var (other, _, otherTransport) = await ConnectAsync();
        await using var otherOwner = otherTransport;

        var checking = tran.ExecuteAsync();
        await other.Strings.SetAsync(guard, "after");

        // whichever way the race lands, the transaction must not have applied when it reports false
        var executed = await checking;
        if (!executed)
        {
            await Assert.ThrowsAnyAsync<Exception>(async () => await pending);
            Assert.False(await context.Keys.ExistsAsync(counter));
        }
        else
        {
            Assert.Equal(1, await pending);
        }
    }

    [Fact]
    public async Task ASetAndGetTravelTheNewStackToARealServer()
    {
        RespDatabaseContext context;
        StreamDuplexTransport transport;
        try
        {
            (context, _, transport) = await ConnectAsync();
        }
        catch (SocketException ex)
        {
            Assert.Skip($"Unable to connect to server: {ex.Message}");
            return;
        }

        await using var owner = transport;

        RedisKey key = "RespNewStackEndToEndTests:counter";
        await context.Keys.DeleteAsync(key);
        await context.Strings.SetAsync(key, 41);

        Assert.Equal(42, (long)await context.Strings.IncrementAsync(key));
        Assert.Equal("42", (string?)await context.Strings.GetAsync(key));

        output.WriteLine("round-tripped through socket → transport → connection → operation, no Message");
    }

    [Fact]
    public async Task PipeliningHoldsAgainstARealServer()
    {
        // the reason any of this exists: many requests in flight, each matched to its own caller. A fake
        // transport cannot show this, because it never reorders or coalesces reads the way a socket does.
        RespDatabaseContext context;
        StreamDuplexTransport transport;
        try
        {
            (context, _, transport) = await ConnectAsync();
        }
        catch (SocketException ex)
        {
            Assert.Skip($"Unable to connect to server: {ex.Message}");
            return;
        }

        await using var owner = transport;

        RedisKey key = "RespNewStackEndToEndTests:pipeline";
        await context.Keys.DeleteAsync(key);

        const int Count = 500;
        var pending = new ValueTask<long>[Count];
        for (var i = 0; i < Count; i++) pending[i] = context.Strings.IncrementAsync(key);

        // every reply is the increment for its own position, in order; a desynchronised queue shows up
        // here as an off-by-one that no in-memory test would catch
        for (var i = 0; i < Count; i++) Assert.Equal(i + 1, await pending[i]);
    }

    [Fact]
    public async Task AServerErrorArrivesAsARedisServerException()
    {
        RespDatabaseContext context;
        StreamDuplexTransport transport;
        try
        {
            (context, _, transport) = await ConnectAsync();
        }
        catch (SocketException ex)
        {
            Assert.Skip($"Unable to connect to server: {ex.Message}");
            return;
        }

        await using var owner = transport;

        RedisKey key = "RespNewStackEndToEndTests:wrongtype";
        await context.Keys.DeleteAsync(key);
        await context.Lists.LeftPushAsync(key, "a");

        // INCR against a list: the server says WRONGTYPE, and the caller sees the exception it would
        // have seen from the old pipeline
        var ex2 = await Assert.ThrowsAsync<RedisServerException>(async () => await context.Strings.IncrementAsync(key));
        Assert.StartsWith("WRONGTYPE", ex2.Message);
    }

    [Fact]
    public async Task TheOperationRecordsWhereItWentAndWhatMoved()
    {
        // "not cheating by omitting half the work": status, connection, byte stamps and the write tick,
        // recorded against a real socket rather than asserted against a fake
        RespConnection connection;
        RespDatabaseContext context;
        StreamDuplexTransport transport;
        try
        {
            (context, connection, transport) = await ConnectAsync();
        }
        catch (SocketException ex)
        {
            Assert.Skip($"Unable to connect to server: {ex.Message}");
            return;
        }

        await using var owner = transport;

        await context.Strings.GetAsync("RespNewStackEndToEndTests:diagnostics");

        Assert.True(connection.BytesSent > 0);
        Assert.True(connection.BytesReceived > 0);
        Assert.Equal(0, connection.PendingCount);
        Assert.False(connection.IsClosed);
        output.WriteLine($"sent {connection.BytesSent} bytes, received {connection.BytesReceived}");
    }
}
