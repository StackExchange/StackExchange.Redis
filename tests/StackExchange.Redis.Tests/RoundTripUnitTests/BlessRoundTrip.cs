using System;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests.RoundTripUnitTests;

/// <summary>
/// Wire bytes, routing and retry category for the <c>BLESS</c> subcommands. <c>BLESS</c> itself is replica-eligible,
/// so the write subcommands have to be raised to primary-only by the message factory.
/// </summary>
public class BlessRoundTrip(ITestOutputHelper log)
{
    [Theory(Timeout = 1000)]
    [InlineData("SET", "*4\r\n$5\r\nBLESS\r\n$3\r\nSET\r\n$3\r\nkey\r\n$8\r\nNO-EVICT\r\n", ":1\r\n", true)]
    [InlineData("SET", "*4\r\n$5\r\nBLESS\r\n$3\r\nSET\r\n$3\r\nkey\r\n$8\r\nNO-EVICT\r\n", ":0\r\n", false)]
    [InlineData("CLEAR", "*4\r\n$5\r\nBLESS\r\n$5\r\nCLEAR\r\n$3\r\nkey\r\n$8\r\nNO-EVICT\r\n", ":1\r\n", true)]
    public async Task SetClear_RoundTrips(string subcommand, string requestResp, string responseResp, bool expected)
    {
        var msg = RedisDatabase.CreateBlessMessage(0, subcommand == "SET" ? RedisLiterals.SET : RedisLiterals.CLEAR, "key", BlessFlags.NoEvict, CommandFlags.None);
        var result = await TestConnection.ExecuteAsync(msg, ResultProcessor.Boolean, requestResp, responseResp, log: log);
        Assert.Equal(expected, result);
    }

    [Theory(Timeout = 1000)]
    [InlineData("*1\r\n$8\r\nNO-EVICT\r\n", BlessFlags.NoEvict)]
    [InlineData("*0\r\n", BlessFlags.None)]
    public async Task Get_RoundTrips(string responseResp, BlessFlags expected)
    {
        var msg = RedisDatabase.CreateBlessFlagsMessage(0, "key", CommandFlags.None);
        const string requestResp = "*3\r\n$5\r\nBLESS\r\n$3\r\nGET\r\n$3\r\nkey\r\n";
        var result = await TestConnection.ExecuteAsync(msg, ResultProcessor.BlessFlags, requestResp, responseResp, log: log);
        Assert.Equal(expected, result);
    }

    [Fact(Timeout = 1000)]
    public async Task Scan_RoundTrips()
    {
        // COUNT is always sent: the server's default (1024) is not the library's page size
        var msg = new RedisServer.BlessScanMessage(0, CommandFlags.None, 0, BlessFlags.NoEvict, 250);
        const string requestResp = "*6\r\n$5\r\nBLESS\r\n$4\r\nSCAN\r\n$1\r\n0\r\n$8\r\nNO-EVICT\r\n$5\r\nCOUNT\r\n$3\r\n250\r\n";
        // reuses the SCAN reply processor; this only asserts the outbound bytes
        await TestConnection.ExecuteAsync(msg, ResultProcessor.DemandOK, requestResp, "+OK\r\n", log: log);
    }

    [Fact]
    public void WritesArePrimaryOnly_ReadsAreNot()
    {
        Assert.False(RedisCommand.BLESS.IsPrimaryOnly());
        Assert.True(RedisDatabase.CreateBlessMessage(0, RedisLiterals.SET, "key", BlessFlags.NoEvict, CommandFlags.PreferReplica).IsPrimaryOnly());
        Assert.True(RedisDatabase.CreateBlessMessage(0, RedisLiterals.CLEAR, "key", BlessFlags.NoEvict, CommandFlags.PreferReplica).IsPrimaryOnly());
        Assert.False(RedisDatabase.CreateBlessFlagsMessage(0, "key", CommandFlags.PreferReplica).IsPrimaryOnly());
    }

    [Fact]
    public void WriteDemandingReplicaFails()
        => Assert.Throws<RedisCommandException>(() => RedisDatabase.CreateBlessMessage(0, RedisLiterals.SET, "key", BlessFlags.NoEvict, CommandFlags.DemandReplica));

    [Fact]
    public void RetryCategories()
    {
        // flag state converges, so a replayed SET/CLEAR leaves the same end-state
        Assert.Equal(CommandFlags.CommandRetryWriteChecked, RedisDatabase.CreateBlessMessage(0, RedisLiterals.SET, "key", BlessFlags.NoEvict, CommandFlags.None).Flags & Message.MaskRetryCategory);
        Assert.Equal(CommandFlags.CommandRetryReadOnly, RedisDatabase.CreateBlessFlagsMessage(0, "key", CommandFlags.None).Flags & Message.MaskRetryCategory);
    }

    [Theory]
    [InlineData(BlessFlags.None)]
    [InlineData((BlessFlags)2)]
    [InlineData(BlessFlags.NoEvict | (BlessFlags)4)]
    public void NoOrUnknownFlagsAreRejected(BlessFlags bless)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RedisDatabase.CreateBlessMessage(0, RedisLiterals.SET, "key", bless, CommandFlags.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RedisServer.BlessScanMessage(0, CommandFlags.None, 0, bless, 10));
    }
}
