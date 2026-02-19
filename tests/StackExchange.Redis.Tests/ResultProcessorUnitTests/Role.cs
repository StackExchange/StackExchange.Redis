using Xunit;

namespace StackExchange.Redis.Tests.ResultProcessorUnitTests;

public class RoleTests(ITestOutputHelper log) : ResultProcessorUnitTest(log)
{
    [Fact]
    public void Role_Master_NoReplicas()
    {
        // 1) "master"
        // 2) (integer) 3129659
        // 3) (empty array)
        var resp = "*3\r\n$6\r\nmaster\r\n:3129659\r\n*0\r\n";
        var processor = ResultProcessor.Role;
        var result = Execute(resp, processor);

        Assert.NotNull(result);
        var master = Assert.IsType<Role.Master>(result);
        Assert.Equal("master", master.Value);
        Assert.Equal(3129659, master.ReplicationOffset);
        Assert.NotNull(master.Replicas);
        Assert.Empty(master.Replicas);
    }

    [Fact]
    public void Role_Master_WithReplicas()
    {
        // 1) "master"
        // 2) (integer) 3129659
        // 3) 1) 1) "127.0.0.1"
        //       2) "9001"
        //       3) "3129242"
        //    2) 1) "127.0.0.1"
        //       2) "9002"
        //       3) "3129543"
        var resp = "*3\r\n" +
                   "$6\r\nmaster\r\n" +
                   ":3129659\r\n" +
                   "*2\r\n" +
                   "*3\r\n$9\r\n127.0.0.1\r\n$4\r\n9001\r\n$7\r\n3129242\r\n" +
                   "*3\r\n$9\r\n127.0.0.1\r\n$4\r\n9002\r\n$7\r\n3129543\r\n";
        var processor = ResultProcessor.Role;
        var result = Execute(resp, processor);

        Assert.NotNull(result);
        var master = Assert.IsType<Role.Master>(result);
        Assert.Equal("master", master.Value);
        Assert.Equal(3129659, master.ReplicationOffset);
        Assert.NotNull(master.Replicas);
        Assert.Equal(2, master.Replicas.Count);

        var replicas = new System.Collections.Generic.List<Role.Master.Replica>(master.Replicas);
        Assert.Equal("127.0.0.1", replicas[0].Ip);
        Assert.Equal(9001, replicas[0].Port);
        Assert.Equal(3129242, replicas[0].ReplicationOffset);

        Assert.Equal("127.0.0.1", replicas[1].Ip);
        Assert.Equal(9002, replicas[1].Port);
        Assert.Equal(3129543, replicas[1].ReplicationOffset);
    }

    [Theory]
    [InlineData("slave")]
    [InlineData("replica")]
    public void Role_Replica_Connected(string roleType)
    {
        // 1) "slave" (or "replica")
        // 2) "127.0.0.1"
        // 3) (integer) 9000
        // 4) "connected"
        // 5) (integer) 3167038
        var resp = $"*5\r\n${roleType.Length}\r\n{roleType}\r\n$9\r\n127.0.0.1\r\n:9000\r\n$9\r\nconnected\r\n:3167038\r\n";
        var processor = ResultProcessor.Role;
        var result = Execute(resp, processor);

        Assert.NotNull(result);
        var replica = Assert.IsType<Role.Replica>(result);
        Assert.Equal(roleType, replica.Value);
        Assert.Equal("127.0.0.1", replica.MasterIp);
        Assert.Equal(9000, replica.MasterPort);
        Assert.Equal("connected", replica.State);
        Assert.Equal(3167038, replica.ReplicationOffset);
    }

    [Theory]
    [InlineData("connect")]
    [InlineData("connecting")]
    [InlineData("sync")]
    [InlineData("connected")]
    [InlineData("none")]
    [InlineData("handshake")]
    public void Role_Replica_VariousStates(string state)
    {
        var resp = $"*5\r\n$5\r\nslave\r\n$9\r\n127.0.0.1\r\n:9000\r\n${state.Length}\r\n{state}\r\n:3167038\r\n";
        var processor = ResultProcessor.Role;
        var result = Execute(resp, processor);

        Assert.NotNull(result);
        var replica = Assert.IsType<Role.Replica>(result);
        Assert.Equal(state, replica.State);
    }

    [Fact]
    public void Role_Sentinel()
    {
        // 1) "sentinel"
        // 2) 1) "resque-master"
        //    2) "html-fragments-master"
        //    3) "stats-master"
        //    4) "metadata-master"
        var resp = "*2\r\n" +
                   "$8\r\nsentinel\r\n" +
                   "*4\r\n" +
                   "$13\r\nresque-master\r\n" +
                   "$21\r\nhtml-fragments-master\r\n" +
                   "$12\r\nstats-master\r\n" +
                   "$15\r\nmetadata-master\r\n";
        var processor = ResultProcessor.Role;
        var result = Execute(resp, processor);

        Assert.NotNull(result);
        var sentinel = Assert.IsType<Role.Sentinel>(result);
        Assert.Equal("sentinel", sentinel.Value);
        Assert.NotNull(sentinel.MonitoredMasters);
        Assert.Equal(4, sentinel.MonitoredMasters.Count);

        var masters = new System.Collections.Generic.List<string?>(sentinel.MonitoredMasters);
        Assert.Equal("resque-master", masters[0]);
        Assert.Equal("html-fragments-master", masters[1]);
        Assert.Equal("stats-master", masters[2]);
        Assert.Equal("metadata-master", masters[3]);
    }

    [Theory]
    [InlineData("unknown", false)] // Short value - tests TryGetSpan path
    [InlineData("unknown", true)]
    [InlineData("long_value_to_test_buffer_size", true)] // Streaming scalar - tests Buffer path (TryGetSpan fails on non-contiguous)
    public void Role_Unknown(string roleName, bool streaming)
    {
        var resp = streaming
            ? $"*1\r\n$?\r\n;{roleName.Length}\r\n{roleName}\r\n;6\r\n_extra\r\n;0\r\n" // force an extra chunk
            : $"*1\r\n${roleName.Length}\r\n{roleName}\r\n";
        var processor = ResultProcessor.Role;
        var result = Execute(resp, processor);

        Assert.NotNull(result);
        var unknown = Assert.IsType<Role.Unknown>(result);
        Assert.Equal(roleName + (streaming ? "_extra" : ""), unknown.Value);
    }

    [Fact]
    public void Role_EmptyArray_ReturnsNull()
    {
        var resp = "*0\r\n";
        var processor = ResultProcessor.Role;
        var result = Execute(resp, processor);
        Assert.Null(result);
    }
}
