using System.IO;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests.Issues;

public class DefaultDatabaseTests : TestBase
{
    public DefaultDatabaseTests(ITestOutputHelper output) : base(output) { }

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
    public void ConfigurationOptions_UnspecifiedDefaultDb()
    {
        var log = new StringWriter();
        try
        {
            using var conn = ConnectionMultiplexer.Connect(TestConfig.Current.PrimaryServerAndPort, log);
            var db = conn.GetDatabase();
            Assert.Equal(0, db.Database);
        }
        finally
        {
            Log(log.ToString());
        }
    }

    [Fact]
    public void ConfigurationOptions_SpecifiedDefaultDb()
    {
        var log = new StringWriter();
        try
        {
            using var conn = ConnectionMultiplexer.Connect($"{TestConfig.Current.PrimaryServerAndPort},defaultDatabase=3", log);
            var db = conn.GetDatabase();
            Assert.Equal(3, db.Database);
        }
        finally
        {
            Log(log.ToString());
        }
    }
}
