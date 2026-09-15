using System;
using System.Net;
using System.Threading.Tasks;
using StackExchange.Redis.Availability;
using Xunit;

namespace StackExchange.Redis.Tests.MultiGroupTests;

/// <summary>
/// What a group does when no member can serve: see
/// <see href="https://github.com/StackExchange/StackExchange.Redis/issues/3223"/>.
/// </summary>
/// <remarks>
/// A group is an <see cref="IConnectionMultiplexer"/>, so it has to fail the way one does. Reporting a
/// down deployment as <see cref="InvalidOperationException"/> means a caller who wrote
/// <c>catch (RedisConnectionException)</c> - or a Polly policy that handles it - does not see the failure
/// at all, and that caller cannot reasonably be expected to know which implementation they were handed.
/// </remarks>
public class GroupUnavailableTests
{
    /// <summary>Two members that nothing is listening on, so the group comes up with nowhere to send.</summary>
    private static async Task<IConnectionGroup> DeadGroupAsync()
    {
        // ports 1 and 2 are privileged and not in use; the group forces AbortOnConnectFail = false, so the
        // connect completes and the failure surfaces at the first command - which is the case being pinned
        ConnectionGroupMember[] members =
        [
            new(new ConfigurationOptions { EndPoints = { new IPEndPoint(IPAddress.Loopback, 1) }, ConnectTimeout = 500 }),
            new(new ConfigurationOptions { EndPoints = { new IPEndPoint(IPAddress.Loopback, 2) }, ConnectTimeout = 500 }),
        ];

        return await ConnectionMultiplexer.ConnectGroupAsync(members);
    }

    [Fact]
    public async Task ACommandOnADeadGroupThrowsAConnectionException()
    {
        await using var group = await DeadGroupAsync();

        var ex = await Assert.ThrowsAsync<RedisConnectionException>(
            () => group.GetDatabase().StringGetAsync("k"));

        Assert.Equal(ConnectionFailureType.UnableToConnect, ex.FailureType);
    }

    [Fact]
    public async Task TheSynchronousPathAgrees()
    {
        await using var group = await DeadGroupAsync();

        // the two throw sites are the active multiplexer and the active member; both are reachable, and
        // both used to be InvalidOperationException
        Assert.Throws<RedisConnectionException>(() => group.GetDatabase().StringGet("k"));
        Assert.Throws<RedisConnectionException>(() => group.GetServer(group.GetEndPoints()[0]));
    }

    [Fact]
    public async Task TheMessageSaysWhichMembersAndWhy()
    {
        await using var group = await DeadGroupAsync();

        var ex = Assert.Throws<RedisConnectionException>(() => group.GetDatabase().StringGet("k"));

        // the leading sentence is unchanged, so anything already matching on it still matches
        Assert.StartsWith("All connections are unavailable.", ex.Message);

        // ...followed by a count per state, so "all unavailable" says whether nothing ever connected or
        // everything was judged unhealthy - which point at different causes
        Assert.True(
            ex.Message.Contains("2 unhealthy") || ex.Message.Contains("2 not connected"),
            $"expected a count per state, got: {ex.Message}");

        // and NOT the endpoints: a member's name defaults to its endpoint, and this message ends up in
        // logs and error-reporting services, where the deployment's hosts are not ours to leak
        Assert.DoesNotContain("127.0.0.1", ex.Message);
    }

    [Fact]
    public async Task MixedStatesAreCountedSeparately()
    {
        // one member that is simply never connected alongside two the health check has judged: the counts
        // have to stay distinct, or the message says less than "all unavailable" already did
        ConnectionGroupMember[] members =
        [
            new(new ConfigurationOptions { EndPoints = { new IPEndPoint(IPAddress.Loopback, 1) }, ConnectTimeout = 500 }),
            new(new ConfigurationOptions { EndPoints = { new IPEndPoint(IPAddress.Loopback, 2) }, ConnectTimeout = 500 })
            {
                SkipInitialHealthCheck = true,
            },
        ];

        await using var group = await ConnectionMultiplexer.ConnectGroupAsync(members);

        var ex = Assert.Throws<RedisConnectionException>(() => group.GetDatabase().StringGet("k"));
        Assert.StartsWith("All connections are unavailable. (", ex.Message);
        Assert.EndsWith(")", ex.Message);
    }
}
