using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests.Issues;

public class DefaultDatabaseTests(ITestOutputHelper output) : TestBase(output)
{
    [Fact]
    public void UnspecifiedDbId_ReturnsNull()
    {
        var config = ConfigurationOptions.Parse("localhost");
        Assert.Null(config.DefaultDatabase);
    }

    [Fact]
    public void SpecifiedDbId_ReturnsExpected()
    {
        var config = ConfigurationOptions.Parse("localhost,defaultDatabase=3");
        Assert.Equal(3, config.DefaultDatabase);
    }

    [Fact]
    public async Task ConfigurationOptions_UnspecifiedDefaultDb()
    {
        var log = new StringWriter();
        try
        {
            await using var conn = await ConnectionMultiplexer.ConnectAsync(TestConfig.Current.PrimaryServerAndPort, log);
            var db = conn.GetDatabase();
            Assert.Equal(0, db.Database);
        }
        finally
        {
            Log(log.ToString());
        }
    }

    [Fact]
    public async Task ConfigurationOptions_SpecifiedDefaultDb()
    {
        var log = new StringWriter();
        try
        {
            await using var conn = await ConnectionMultiplexer.ConnectAsync($"{TestConfig.Current.PrimaryServerAndPort},defaultDatabase=3", log);
            var db = conn.GetDatabase();
            Assert.Equal(3, db.Database);
        }
        finally
        {
            Log(log.ToString());
        }
    }
}
