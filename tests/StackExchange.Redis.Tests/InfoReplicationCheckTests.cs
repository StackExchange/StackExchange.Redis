using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

public class InfoReplicationCheckTests(ITestOutputHelper output) : TestBase(output)
{
    protected override string GetConfiguration() => base.GetConfiguration() + ",configCheckSeconds=2";

    [Fact]
    public async Task Exec()
    {
        Assert.Skip("need to think about CompletedSynchronously");

        using var conn = Create();

        var parsed = ConfigurationOptions.Parse(conn.Configuration);
        Assert.Equal(2, parsed.ConfigCheckSeconds);
        var before = conn.GetCounters();
        await Task.Delay(7000).ForAwait();
        var after = conn.GetCounters();
        int done = (int)(after.Interactive.CompletedSynchronously - before.Interactive.CompletedSynchronously);
        Assert.True(done >= 2, $"expected >=2, got {done}");
    }
}
