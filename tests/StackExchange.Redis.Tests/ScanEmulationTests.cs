using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// What a scan does when the server - or the command map - does not offer <c>SCAN</c>.
/// </summary>
/// <remarks>
/// <para>
/// The shipped surface has always degraded to reading the whole structure in one reply
/// (<c>HGETALL</c>, <c>HKEYS</c>, <c>SMEMBERS</c>, <c>ZRANGE</c>), and refusing the two things that
/// cannot be honoured that way. Moving the surface must not quietly drop that, or a deployment on a
/// pre-2.8 server stops working at the point it upgrades the client.
/// </para>
/// <para>
/// <b>These exist because nothing else exercises the path.</b> Every test server supports <c>SCAN</c>,
/// so the emulation is unreachable from the integration suite - it would have shipped untested. A
/// command map with the scan commands removed is the only honest way to reach it.
/// </para>
/// </remarks>
public class ScanEmulationTests
{
    private static readonly CommandMap NoScan = CommandMap.Create(
        new HashSet<string> { "HSCAN", "SSCAN", "ZSCAN" }, available: false);

    private static IDatabase Target(FakeExecutor executor)
        => new RedisDatabase(
            new RespDatabaseContext(new RespContext(NoScan).WithExecutor(executor)), null!, null);

    [Fact]
    public void AHashScanReadsEverythingInOneReply()
    {
        var executor = new FakeExecutor("*4\r\n$1\r\na\r\n$1\r\n1\r\n$1\r\nb\r\n$1\r\n2\r\n");

        var entries = Target(executor).HashScan("k").ToArray();

        Assert.Equal("*2|$7|HGETALL|$1|k|", Assert.Single(executor.Sent));
        Assert.Equal(["a", "b"], entries.Select(x => (string?)x.Name));
        Assert.Equal(["1", "2"], entries.Select(x => (string?)x.Value));
    }

    [Fact]
    public void ASetScanReadsEverythingInOneReply()
    {
        var executor = new FakeExecutor("*2\r\n$1\r\nx\r\n$1\r\ny\r\n");

        var members = Target(executor).SetScan("k").ToArray();

        Assert.Equal("*2|$8|SMEMBERS|$1|k|", Assert.Single(executor.Sent));
        Assert.Equal(["x", "y"], members.Select(x => (string?)x));
    }

    /// <summary>The page offset still applies: it is an offset into the single page.</summary>
    [Fact]
    public void ThePageOffsetSkipsWithinTheOnePage()
    {
        var executor = new FakeExecutor("*3\r\n$1\r\nx\r\n$1\r\ny\r\n$1\r\nz\r\n");

        var members = Target(executor).SetScan("k", pageOffset: 2).ToArray();

        Assert.Equal(["z"], members.Select(x => (string?)x));
    }

    /// <summary>
    /// A non-origin cursor is refused rather than silently restarted: there is no cursor to resume from,
    /// and returning the first page again would be a wrong answer rather than a missing one.
    /// </summary>
    [Fact]
    public void ResumingACursorIsRefused()
    {
        var ex = Assert.Throws<RedisCommandException>(
            () => Target(new FakeExecutor("*0\r\n")).HashScan("k", cursor: 5).ToArray());

        Assert.Contains("cursor", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>And a pattern is refused, because the whole-structure commands do not filter.</summary>
    [Fact]
    public void APatternIsRefused()
        => Assert.Throws<RedisCommandException>(
            () => Target(new FakeExecutor("*0\r\n")).HashScan("k", pattern: "a*").ToArray());

    [Fact]
    public async Task TheAsyncFormDegradesTheSameWay()
    {
        var executor = new FakeExecutor("*2\r\n$1\r\nx\r\n$1\r\ny\r\n");

        var seen = new List<string?>();
        await foreach (var member in Target(executor).SetScanAsync("k"))
        {
            seen.Add((string?)member);
        }

        Assert.Equal("*2|$8|SMEMBERS|$1|k|", Assert.Single(executor.Sent));
        Assert.Equal(["x", "y"], seen);
    }
}
