using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using StackExchange.Redis.Availability;
using StackExchange.Redis.Maintenance;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Events that a group forwards from its members arrive with the member's own connection as the sender; a subscriber
/// must be able to say which member that was, without the connection itself being part of the public surface.
/// </summary>
public class GroupMemberSenderUnitTests(ITestOutputHelper log)
{
    [Fact]
    public async Task ForwardedEventsAreAttributedToTheirMember()
    {
        using var server0 = new InProcessTestServer(log);
        using var server1 = new InProcessTestServer(log);
        var member0 = new ConnectionGroupMember(server0.GetClientConfig(defaultOnly: true), "zero");
        var member1 = new ConnectionGroupMember(server1.GetClientConfig(defaultOnly: true), "one");

        var group = await ConnectionMultiplexer.ConnectGroupAsync(member0, member1);
        await using var _ = (IAsyncDisposable)group;

        var senders = new ConcurrentQueue<object?>();
        group.ServerMaintenanceEvent += (sender, _) => senders.Enqueue(sender);

        // raised exactly as a real notification would be: on the member's own connection, then forwarded by the group
        member0.Multiplexer.OnServerMaintenanceEvent(new AzureMaintenanceEvent("NotificationType|NodeMaintenanceStarting"));
        member1.Multiplexer.OnServerMaintenanceEvent(new AzureMaintenanceEvent("NotificationType|NodeMaintenanceEnded"));

        Assert.Equal(2, senders.Count);
        Assert.True(senders.TryDequeue(out var first));
        Assert.True(senders.TryDequeue(out var second));

        Assert.True(member0.IsSenderOf(first));
        Assert.False(member1.IsSenderOf(first));
        Assert.Same(member0, group.FindMember(first));

        Assert.True(member1.IsSenderOf(second));
        Assert.False(member0.IsSenderOf(second));
        Assert.Same(member1, group.FindMember(second));

        // "which member is this from" is a different question to "is this about what I am using right now", but
        // the latter is now expressible
        var active = group.ActiveMember;
        Assert.NotNull(active);
        Assert.True(active.IsSenderOf(first) ^ active.IsSenderOf(second));
    }

    [Fact]
    public async Task NonMemberSendersAreNotClaimed()
    {
        using var server0 = new InProcessTestServer(log);
        using var server1 = new InProcessTestServer(log);
        var member0 = new ConnectionGroupMember(server0.GetClientConfig(defaultOnly: true), "zero");
        var member1 = new ConnectionGroupMember(server1.GetClientConfig(defaultOnly: true), "one");

        var group = await ConnectionMultiplexer.ConnectGroupAsync(member0, member1);
        await using var _ = (IAsyncDisposable)group;

        Assert.False(member0.IsSenderOf(null));
        Assert.Null(group.FindMember(null));

        // the group itself is not any one member, even the active one
        Assert.False(member0.IsSenderOf(group));
        Assert.False(member1.IsSenderOf(group));
        Assert.Null(group.FindMember(group));

        Assert.False(member0.IsSenderOf(new object()));

        // a member is the sender of anything it raises itself
        Assert.True(member0.IsSenderOf(member0));
        Assert.False(member0.IsSenderOf(member1));
    }

    [Fact]
    public async Task RemovedMemberNoLongerClaimsItsConnection()
    {
        using var server0 = new InProcessTestServer(log);
        using var server1 = new InProcessTestServer(log);
        var member0 = new ConnectionGroupMember(server0.GetClientConfig(defaultOnly: true), "zero");
        var member1 = new ConnectionGroupMember(server1.GetClientConfig(defaultOnly: true), "one");

        var group = await ConnectionMultiplexer.ConnectGroupAsync(member0, member1);
        await using var _ = (IAsyncDisposable)group;

        var connection = member1.Multiplexer;
        Assert.True(member1.IsSenderOf(connection));
        Assert.Same(member1, group.FindMember(connection));

        Assert.True(group.Remove(member1));

        Assert.False(member1.IsSenderOf(connection));
        Assert.Null(group.FindMember(connection));
    }
}
