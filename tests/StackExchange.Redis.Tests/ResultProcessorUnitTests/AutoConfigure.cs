using System;
using System.Net;
using System.Text;
using RESPite.Messages;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests.ResultProcessorUnitTests;

/// <summary>
/// The parsing half of what the shipped <c>AutoConfigureProcessor</c> did.
/// </summary>
/// <remarks>
/// <para>
/// The shipped processor parsed <i>and</i> applied (server version, type, role, connection id) in one
/// step, so without a live connection every one of these tests could only assert that it threw. The new
/// core splits the two: <c>RespHandshake</c> asks each question with its own handler and applies the
/// answer itself - so the parse is now reachable, and these pin what each reply is read as. The
/// applying half (<c>server.IsReplica = ...</c> and so on) lives in <c>RespHandshake</c>'s async
/// discovery methods, which need a connected context and are covered by the integration suite.
/// </para>
/// </remarks>
public class AutoConfigure(ITestOutputHelper log) : ResultProcessorUnitTest(log)
{
    private const string ReplicationInfo =
        "# Replication\r\n" +
        "role:master\r\n" +
        "connected_slaves:0\r\n" +
        "master_failover_state:no-failover\r\n" +
        "master_replid:8c3e3c3e3c3e3c3e3c3e3c3e3c3e3c3e3c3e3c3e\r\n" +
        "master_replid2:0000000000000000000000000000000000000000\r\n" +
        "master_repl_offset:0\r\n" +
        "second_repl_offset:-1\r\n" +
        "repl_backlog_active:0\r\n" +
        "repl_backlog_size:1048576\r\n" +
        "repl_backlog_first_byte_offset:0\r\n" +
        "repl_backlog_histlen:0\r\n";

    private const string ServerInfo =
        "# Server\r\n" +
        "redis_version:7.2.4\r\n" +
        "redis_git_sha1:00000000\r\n" +
        "redis_mode:standalone\r\n" +
        "os:Linux 5.15.0-1-amd64 x86_64\r\n" +
        "arch_bits:64\r\n";

    private static string Bulk(string payload) => $"${payload.Length}\r\n{payload}\r\n";

    private static object? Replication(string resp) => PrivateHandlers.ParseBoxed(typeof(RespHandshake), "ReplicationHandler", resp);

    private static object? Product(string resp) => PrivateHandlers.ParseBoxed(typeof(RespHandshake), "ProductHandler", resp);

    private static IRespHandler<Version?> ServerVersion => PrivateHandlers.Get<Version?>(typeof(RespHandshake), "ServerVersionHandler");

    private static IRespHandler<string?> ConfigSetting => PrivateHandlers.Get<string?>(typeof(RespHandshake), "ConfigSettingHandler");

    [Fact]
    public void ClientId_Integer_Success()
    {
        // CLIENT ID response; the handshake sends it as SendAsync<long>, i.e. the default Int64 handler
        var resp = ":11\r\n";
        Assert.Equal(11, Execute(resp, RespHandlers.Int64));
    }

    [Fact]
    public void Info_BulkString_Success()
    {
        // INFO replication, as DiscoverReplicationAsync reads it
        var reply = Replication(Bulk(ReplicationInfo));

        Assert.False(PrivateHandlers.Property<bool?>(reply, "IsReplica"));
        Assert.Null(PrivateHandlers.Property<EndPoint?>(reply, "Primary")); // a primary names no master_host
    }

    [Fact]
    public void Info_Replica_NamesPrimary()
    {
        var info = "# Replication\r\nrole:slave\r\nmaster_host:10.0.0.1\r\nmaster_port:6379\r\nmaster_link_status:up\r\n";
        var reply = Replication(Bulk(info));

        Assert.True(PrivateHandlers.Property<bool?>(reply, "IsReplica"));
        var primary = Assert.IsType<IPEndPoint>(PrivateHandlers.Property<EndPoint?>(reply, "Primary"));
        Assert.Equal("10.0.0.1", primary.Address.ToString());
        Assert.Equal(6379, primary.Port);
    }

    [Fact]
    public void Info_WithVersion_Success()
    {
        // INFO server: read twice by the handshake - for the product, and for the version alone
        var resp = Bulk(ServerInfo);

        var product = Product(resp);
        Assert.Equal(ProductVariant.Redis, PrivateHandlers.Property<ProductVariant>(product, "Variant"));
        Assert.Equal("7.2.4", PrivateHandlers.Property<string>(product, "ProductVersion"));
        Assert.Equal(ServerType.Standalone, PrivateHandlers.Property<ServerType?>(product, "ServerType"));

        Assert.Equal(new Version(7, 2, 4), Execute(resp, ServerVersion));
    }

    [Theory]
    [InlineData("$0\r\n\r\n")] // empty INFO
    [InlineData("$-1\r\n")] // null INFO
    public void Info_EmptyOrNull_SaysNothing(string resp)
    {
        // the shipped processor threw here only for want of a connection; the handlers say "nothing known"
        var replication = Replication(resp);
        Assert.Null(PrivateHandlers.Property<bool?>(replication, "IsReplica"));
        Assert.Null(PrivateHandlers.Property<EndPoint?>(replication, "Primary"));

        var product = Product(resp);
        Assert.Null(PrivateHandlers.Property<ServerType?>(product, "ServerType"));

        Assert.Null(Execute(resp, ServerVersion));
    }

    [Fact]
    public void Config_Array_Success()
    {
        // CONFIG GET timeout response
        var resp = "*2\r\n" +
                   "$7\r\ntimeout\r\n" +
                   "$3\r\n300\r\n";

        Assert.Equal("300", Execute(resp, ConfigSetting));
    }

    [Fact]
    public void Config_Map_Success()
    {
        // the RESP3 spelling of the same reply
        var resp = "%1\r\n$7\r\ntimeout\r\n$3\r\n300\r\n";
        Assert.Equal("300", Execute(resp, ConfigSetting));
    }

    [Fact]
    public void Config_Empty_Null()
    {
        // no such setting: not an error
        Assert.Null(Execute("*0\r\n", ConfigSetting));
    }

    [Fact]
    public void ReadonlyError_Success()
    {
        // READONLY error response. The new core turns an error reply into RedisServerException before any
        // handler sees it (RespPayloadOperation.ParseFrame), classifying it with RedisErrorKindMetadata -
        // and ProbeReplicaAsync reads the role from exactly that classification.
        var resp = "-READONLY You can't write against a read only replica.\r\n";
        var reader = new RespReader(Encoding.UTF8.GetBytes(resp));

        Assert.True(reader.TryMoveNext(checkError: false));
        Assert.True(reader.IsError);
        Assert.Equal(RedisErrorKind.ReadOnly, RedisErrorKindMetadata.Classify(reader));
    }
}
