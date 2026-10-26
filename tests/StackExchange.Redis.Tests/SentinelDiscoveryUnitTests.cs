using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

public class SentinelDiscoveryUnitTests
{
    [Fact]
    public async Task FirstAnswerDoesNotWaitForASlowSentinel()
    {
        var never = new TaskCompletionSource<string?>();
        var answer = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var query = ConnectionMultiplexer.FirstSentinelResultAsync(
            [never.Task, Task.FromResult<string?>(null), Task.FromException<string?>(new InvalidOperationException()), answer.Task], 10000);
        Assert.False(query.IsCompleted);
        answer.SetResult("primary");
        Assert.Same(query, await Task.WhenAny(query, Task.Delay(5000)));
        Assert.Equal("primary", await query);
        Assert.False(never.Task.IsCompleted);
    }

    [Fact]
    public async Task NoAnswerHasOneDeadlineForTheWholeQuery()
    {
        var never = new TaskCompletionSource<string?>();
        var query = ConnectionMultiplexer.FirstSentinelResultAsync([never.Task, never.Task], 20);
        Assert.Same(query, await Task.WhenAny(query, Task.Delay(5000)));
        Assert.Null(await query);
        Assert.Null(await ConnectionMultiplexer.FirstSentinelResultAsync(Array.Empty<Task<string?>>(), 20));
    }

    [Fact]
    public void EveryRouteIsQueriedConnectedFirstAndReportedDownLast()
    {
        // sentinel-clients Step 1: no route is left out, a dead route only moves back in the launch order
        string[] routes = ["dead-a", "service-a", "down-b", "service-b", "pod-c", "service-c"];
        var ordered = ConnectionMultiplexer.OrderSentinelQueryTargets(routes, s => s.StartsWith("service-"), s => s.StartsWith("down-"));
        Assert.Equal(["service-a", "service-b", "service-c", "dead-a", "pod-c", "down-b"], ordered);
        Assert.Empty(ConnectionMultiplexer.OrderSentinelQueryTargets(Array.Empty<string>(), _ => true, _ => false));
    }

    [Theory]
    [InlineData("master", true)]
    [InlineData("master,disconnected", true)]
    [InlineData("master,s_down", false)]
    [InlineData("master,o_down,s_down", false)]
    [InlineData("slave", false)]
    [InlineData(null, false)]
    public void OnlyAHealthyMasterIsAValidPrimaryAnswer(string? flags, bool valid)
    {
        var row = new List<KeyValuePair<string, string>>
        {
            new("name", "mymaster"),
            new("ip", "192.0.2.1"),
            new("port", "6379"),
        };
        if (flags is not null) row.Add(new("flags", flags));
        var primary = ConnectionMultiplexer.ParseValidPrimary(row.ToArray());
        if (valid) Assert.Equal(new IPEndPoint(IPAddress.Parse("192.0.2.1"), 6379), primary);
        else Assert.Null(primary);
    }

    [Fact]
    public void IPv6PrimaryAddressIsParsed()
    {
        KeyValuePair<string, string>[] row = [new("name", "m"), new("ip", "2001:db8::1"), new("port", "6379"), new("flags", "master")];
        Assert.Equal(new IPEndPoint(IPAddress.Parse("2001:db8::1"), 6379), ConnectionMultiplexer.ParseValidPrimary(row));
    }

    [Theory]
    [InlineData("192.0.2.1", "1:2")]
    [InlineData("192.0.2.1", "port")]
    [InlineData("192.0.2.1", "")]
    [InlineData("", "6379")]
    public void MalformedAddressIsNotAValidPrimaryAnswer(string ip, string port)
    {
        // a sentinel whose answer has no usable address is passed over, like one that does not know the service
        KeyValuePair<string, string>[] row = [new("name", "mymaster"), new("ip", ip), new("port", port), new("flags", "master")];
        Assert.Null(ConnectionMultiplexer.ParseValidPrimary(row));
    }

    [Fact]
    public void AddressIsReadAsAHostNameNeverAsALocalSocket()
    {
        // "!path" is the configuration spelling of a Unix socket; an address sentinel reports is never one
        KeyValuePair<string, string>[] row = [new("name", "m"), new("ip", "!/tmp/redis.sock"), new("port", "6379"), new("flags", "master")];
        var primary = Assert.IsType<DnsEndPoint>(ConnectionMultiplexer.ParseValidPrimary(row));
        Assert.Equal("!/tmp/redis.sock", primary.Host);
        Assert.Equal(6379, primary.Port);
    }

    [Theory]
    [InlineData("master,s_down", true)]
    [InlineData("master,o_down", true)]
    [InlineData("master,disconnected", false)]
    [InlineData("master", false)]
    public void OnlyDownFlagsDisqualifyAPrimary(string flags, bool down)
        => Assert.Equal(down, ConnectionMultiplexer.IsSentinelPrimaryDown(flags));

    [Theory]
    [InlineData("slave,s_down", true)]
    [InlineData("sentinel,o_down", true)]
    [InlineData("slave,disconnected", true)]
    [InlineData("slave", false)]
    [InlineData(null, false)]
    public void DownFlagsAreExcludedFromManagedDiscovery(string? flags, bool down)
        => Assert.Equal(down, ConnectionMultiplexer.IsSentinelNodeDown(flags));
}
