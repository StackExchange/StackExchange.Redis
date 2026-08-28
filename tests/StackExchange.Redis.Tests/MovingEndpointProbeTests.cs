using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Maintenance;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// The DNS probe behind the <c>MOVING</c> handoff. Tested against an injected resolver rather than real DNS,
/// which is the only way to exercise the case that actually happens: the first answer naming the address we
/// were just told to leave.
/// </summary>
public class MovingEndpointProbeTests(ITestOutputHelper log)
{
    private static readonly IPAddress Retiring = IPAddress.Parse("10.129.228.140");
    private static readonly IPAddress Replacement = IPAddress.Parse("10.252.90.18");
    private static readonly DnsEndPoint Endpoint = new("db.example.cloud.redislabs.com", 13486);

    /// <summary>
    /// Resolves from a script: one entry per call, the last repeating forever.
    /// </summary>
    private sealed class ScriptedResolver(params IPAddress[][] answers)
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);

        public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken)
        {
            var index = Interlocked.Increment(ref _calls) - 1;
            return Task.FromResult(answers[Math.Min(index, answers.Length - 1)]);
        }
    }

    [Fact]
    public async Task DnsTrailingTheNotificationIsPolledThrough()
    {
        // The measured case: DNS named the retiring address for the first 4.4-9.7 seconds after MOVING. A
        // client that accepted the first answer would hand off to the node it was told to leave.
        var resolver = new ScriptedResolver(
            [Retiring],
            [Retiring],
            [Retiring],
            [Replacement]);

        var result = await MovingEndpointProbe.ProbeAsync(
            Endpoint, Retiring, window: TimeSpan.FromSeconds(5), pollInterval: TimeSpan.FromMilliseconds(10),
            resolve: resolver.ResolveAsync, log: log.WriteLine);

        Assert.Equal(new IPEndPoint(Replacement, 13486), result);
        Assert.Equal(4, resolver.Calls);
    }

    [Fact]
    public async Task ReplacementOnTheFirstAnswerIsTakenImmediately()
    {
        var resolver = new ScriptedResolver([Replacement]);

        var result = await MovingEndpointProbe.ProbeAsync(
            Endpoint, Retiring, window: TimeSpan.FromSeconds(5), pollInterval: TimeSpan.FromSeconds(1),
            resolve: resolver.ResolveAsync, log: log.WriteLine);

        Assert.Equal(new IPEndPoint(Replacement, 13486), result);
        Assert.Equal(1, resolver.Calls);
    }

    [Fact]
    public async Task WindowExpiringWithoutAMoveGivesUpRatherThanGuessing()
    {
        // Not a failure to report: the server closes the socket regardless, and the relaxed window covers the
        // reconnect. Guessing an address here would be worse than doing nothing.
        var resolver = new ScriptedResolver([Retiring]);

        var result = await MovingEndpointProbe.ProbeAsync(
            Endpoint, Retiring, window: TimeSpan.FromMilliseconds(120), pollInterval: TimeSpan.FromMilliseconds(20),
            resolve: resolver.ResolveAsync, log: log.WriteLine);

        Assert.Null(result);
        Assert.True(resolver.Calls > 1, $"should have retried within the window, but resolved {resolver.Calls} time(s)");
    }

    [Fact]
    public async Task ZeroWindowStillGetsOneAttempt()
    {
        // "act now" rather than "do nothing": the notifications legitimately carry zero or negative times for
        // a connection that arrived mid-window
        var resolver = new ScriptedResolver([Replacement]);

        var result = await MovingEndpointProbe.ProbeAsync(
            Endpoint, Retiring, window: TimeSpan.Zero, pollInterval: TimeSpan.FromSeconds(1),
            resolve: resolver.ResolveAsync, log: log.WriteLine);

        Assert.Equal(new IPEndPoint(Replacement, 13486), result);
        Assert.Equal(1, resolver.Calls);
    }

    [Fact]
    public async Task ResolutionFailureIsRetriedNotFatal()
    {
        // a DNS blip mid-handoff is when we can least afford to give up
        int calls = 0;
        Task<IPAddress[]> Resolve(string host, CancellationToken cancellationToken)
        {
            calls++;
            return calls switch
            {
                1 => throw new System.Net.Sockets.SocketException(11001), // host not found
                2 => Task.FromResult<IPAddress[]>([Retiring]),
                _ => Task.FromResult<IPAddress[]>([Replacement]),
            };
        }

        var result = await MovingEndpointProbe.ProbeAsync(
            Endpoint, Retiring, window: TimeSpan.FromSeconds(5), pollInterval: TimeSpan.FromMilliseconds(10),
            resolve: Resolve, log: log.WriteLine);

        Assert.Equal(new IPEndPoint(Replacement, 13486), result);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task MultipleAddressesTakeTheOneThatIsNotRetiring()
    {
        // a round-robin record can name both nodes at once mid-move
        var resolver = new ScriptedResolver([Retiring, Replacement]);

        var result = await MovingEndpointProbe.ProbeAsync(
            Endpoint, Retiring, window: TimeSpan.FromSeconds(5), pollInterval: TimeSpan.FromSeconds(1),
            resolve: resolver.ResolveAsync, log: log.WriteLine);

        Assert.Equal(new IPEndPoint(Replacement, 13486), result);
    }

    [Fact]
    public async Task PlacementNotPolicyDecidesWhetherWeWait()
    {
        // The A-record count follows actual proxy placement, not the policy name: an all-master-shards
        // database whose shards share a node resolves to one address, and then there is no sibling to step to
        // and the wait is the only option. Same code path as `single`, which is the point - nothing here reads
        // the policy.
        var resolver = new ScriptedResolver([Retiring], [Retiring], [Replacement]);

        var result = await MovingEndpointProbe.ProbeAsync(
            Endpoint, Retiring, window: TimeSpan.FromSeconds(5), pollInterval: TimeSpan.FromMilliseconds(10),
            resolve: resolver.ResolveAsync, log: log.WriteLine);

        Assert.Equal(new IPEndPoint(Replacement, 13486), result);
        Assert.Equal(3, resolver.Calls); // it waited, because there was nothing else advertised
    }

    [Fact]
    public async Task SiblingIsTakenWithoutWaitingForTheRecordToMove()
    {
        // The common case: several A records, so the first resolution already names a live sibling proxy while
        // the retiring address is *still* advertised. Stepping sideways immediately is correct - any proxy of
        // the same database serves the same data - and it means the poll usually never engages.
        var sibling = IPAddress.Parse("10.246.250.155");
        var resolver = new ScriptedResolver([Retiring, sibling]);

        var result = await MovingEndpointProbe.ProbeAsync(
            Endpoint, Retiring, window: TimeSpan.FromSeconds(5), pollInterval: TimeSpan.FromSeconds(1),
            resolve: resolver.ResolveAsync, log: log.WriteLine);

        Assert.Equal(new IPEndPoint(sibling, 13486), result);
        Assert.Equal(1, resolver.Calls);
    }

    [Fact]
    public async Task CancellationStopsThePoll()
    {
        using var cts = new CancellationTokenSource();
        var resolver = new ScriptedResolver([Retiring]);

        var probe = MovingEndpointProbe.ProbeAsync(
            Endpoint, Retiring, window: TimeSpan.FromMinutes(1), pollInterval: TimeSpan.FromMilliseconds(10),
            resolve: resolver.ResolveAsync, log: log.WriteLine, cancellationToken: cts.Token);

        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => probe);
    }
}
