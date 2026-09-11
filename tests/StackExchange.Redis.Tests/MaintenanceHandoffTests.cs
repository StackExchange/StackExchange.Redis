using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Maintenance;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Deciding what a <c>MOVING</c> means: where to go, or that there is nowhere to go.
/// </summary>
/// <remarks>
/// Tested as a pure decision, with the resolver supplied, because the whole thing turns on DNS *changing* -
/// which no in-process fake can arrange.
/// </remarks>
public class MaintenanceHandoffTests(ITestOutputHelper log)
{
    private static readonly IPAddress Retiring = IPAddress.Parse("10.129.228.140");
    private static readonly IPAddress Replacement = IPAddress.Parse("10.252.90.18");
    private static readonly DnsEndPoint Hostname = new("db.example.cloud.redislabs.com", 13486);

    private static Func<string, CancellationToken, Task<IPAddress[]>> Resolves(params IPAddress[] addresses)
        => (_, _) => Task.FromResult(addresses);

    [Fact]
    public async Task HostnameThatHasMovedIsRecycled()
    {
        var decision = await MaintenanceHandoff.DecideAsync(
            Hostname, successor: null, currentAddress: Retiring,
            window: TimeSpan.FromSeconds(5), pollInterval: TimeSpan.FromMilliseconds(10),
            resolve: Resolves(Replacement), log: log.WriteLine);

        log.WriteLine(decision.ToString());
        Assert.Equal(HandoffAction.Recycle, decision.Action);
        Assert.Equal(new IPEndPoint(Replacement, 13486), decision.Target);
    }

    [Fact]
    public async Task HostnameThatNeverMovesDoesNothing()
    {
        // Not a failure: the server closes the socket regardless, the reconnect re-resolves, and the relaxed
        // window covers the gap. Measured on a real cluster - DNS updated three seconds *after* the close - so
        // this is a normal outcome rather than a defensive branch.
        var decision = await MaintenanceHandoff.DecideAsync(
            Hostname, successor: null, currentAddress: Retiring,
            window: TimeSpan.FromMilliseconds(100), pollInterval: TimeSpan.FromMilliseconds(20),
            resolve: Resolves(Retiring), log: log.WriteLine);

        log.WriteLine(decision.ToString());
        Assert.Equal(HandoffAction.None, decision.Action);
        Assert.Null(decision.Target);
    }

    [Fact]
    public async Task AddressEndpointWithNoSuccessorReconnectsOnTheClock()
    {
        // An address cannot be re-resolved and nothing was named, so a change is undetectable from here - which
        // is exactly the case the contract's half-window rule was written for. The address may be a stable
        // front for a backend that has already moved, and waiting passively means being closed mid-command
        // instead of choosing the moment.
        var decision = await MaintenanceHandoff.DecideAsync(
            new IPEndPoint(Retiring, 13486), successor: null, currentAddress: Retiring,
            window: TimeSpan.FromSeconds(5), pollInterval: TimeSpan.FromMilliseconds(10),
            resolve: Resolves(Replacement), log: log.WriteLine);

        log.WriteLine(decision.ToString());
        Assert.Equal(HandoffAction.RecycleAtHalfWindow, decision.Action);
    }

    [Fact]
    public async Task NamedSuccessorIsUsedDirectly()
    {
        // A named successor skips DNS entirely, which is the point of the field: DNS trails a MOVING by 4.4s to
        // 18.7s while the socket closes at 15.7s to 19.1s, so waiting for it is sometimes waiting too long.
        // Note the resolver here still reports the *old* address, and it is never consulted.
        var successor = new IPEndPoint(Replacement, 13486);
        var decision = await MaintenanceHandoff.DecideAsync(
            Hostname, successor, currentAddress: Retiring,
            window: TimeSpan.FromSeconds(5), pollInterval: TimeSpan.FromMilliseconds(10),
            resolve: Resolves(Retiring), log: log.WriteLine);

        log.WriteLine(decision.ToString());
        Assert.Equal(HandoffAction.MoveTo, decision.Action);
        Assert.Equal(successor, decision.Target);
    }

    [Fact]
    public async Task UnknownCurrentAddressReconnectsOnTheClockRatherThanPolling()
    {
        // With no idea where we are, "has it moved?" is unanswerable, so there is nothing to poll for. A
        // tunnel or a Unix domain socket lands here, and for a tunnel the target genuinely may have moved
        // underneath us - so the half-window reconnect is the only tool, and better than waiting to be closed.
        var decision = await MaintenanceHandoff.DecideAsync(
            Hostname, successor: null, currentAddress: null,
            window: TimeSpan.FromSeconds(5), pollInterval: TimeSpan.FromMilliseconds(10),
            resolve: Resolves(Replacement), log: log.WriteLine);

        log.WriteLine(decision.ToString());
        Assert.Equal(HandoffAction.RecycleAtHalfWindow, decision.Action);
    }

    [Fact]
    public async Task PollingBeatsTheClockWhenWeCanSeeTheAddress()
    {
        // The deliberate divergence from the contract's half-window rule, and the reason for it. Where DNS can
        // be polled we wait for it to actually move rather than reconnecting on a timer: measured across three
        // runs, DNS had moved by half of a 15s window only once (+4.4s), and lagged well past it otherwise
        // (+9.7s, +18.7s) - so reconnecting on the clock would usually land back on the node being retired.
        // Doing nothing when it never moves is also deliberate: the server closes the socket, the reconnect
        // re-resolves, and the relaxed window covers the gap.
        var stillOld = await MaintenanceHandoff.DecideAsync(
            Hostname, successor: null, currentAddress: Retiring,
            window: TimeSpan.FromMilliseconds(120), pollInterval: TimeSpan.FromMilliseconds(20),
            resolve: Resolves(Retiring), log: log.WriteLine);

        Assert.Equal(HandoffAction.None, stillOld.Action);
    }

    [Theory]
    [InlineData(15, 0, 1000)] // a generous window: capped at a second, not a tenth of fifteen
    [InlineData(2, 0, 200)] // a short one: a tenth, so a 2s window never spends more than 200ms
    [InlineData(0, 0, 0)] // "act now"
    public void JitterScalesWithTheWindowAndIsCapped(int windowSeconds, int minMilliseconds, int maxMilliseconds)
    {
        var random = new Random(12345);
        for (int i = 0; i < 200; i++)
        {
            var jitter = MaintenanceHandoff.GetJitter(TimeSpan.FromSeconds(windowSeconds), random);
            Assert.InRange(jitter.TotalMilliseconds, minMilliseconds, maxMilliseconds);
        }
    }
}
