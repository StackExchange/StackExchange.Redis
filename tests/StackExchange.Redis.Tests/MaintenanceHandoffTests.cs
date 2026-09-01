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
    public async Task AddressEndpointWithNoSuccessorHasNothingToDo()
    {
        // An address cannot be re-resolved, and nothing was named: there is no handoff to make. This is the
        // case a cluster deployment would hit, and it is why MOVING must not simply reuse endpoint retirement -
        // there would be nothing to retire *to*.
        var decision = await MaintenanceHandoff.DecideAsync(
            new IPEndPoint(Retiring, 13486), successor: null, currentAddress: Retiring,
            window: TimeSpan.FromSeconds(5), pollInterval: TimeSpan.FromMilliseconds(10),
            resolve: Resolves(Replacement), log: log.WriteLine);

        log.WriteLine(decision.ToString());
        Assert.Equal(HandoffAction.None, decision.Action);
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
    public async Task UnknownCurrentAddressDoesNothingRatherThanGuessing()
    {
        // Recycling here would mean accepting whatever DNS says *now*, which for the first several seconds is
        // the address being retired - so we would hand off to the node we were told to leave.
        var decision = await MaintenanceHandoff.DecideAsync(
            Hostname, successor: null, currentAddress: null,
            window: TimeSpan.FromSeconds(5), pollInterval: TimeSpan.FromMilliseconds(10),
            resolve: Resolves(Replacement), log: log.WriteLine);

        log.WriteLine(decision.ToString());
        Assert.Equal(HandoffAction.None, decision.Action);
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
