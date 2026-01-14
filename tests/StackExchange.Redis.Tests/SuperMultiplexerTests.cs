using System.IO;
using System.Threading.Tasks;
using StackExchange.Redis.Tests.Helpers;
using Xunit;

namespace StackExchange.Redis.Tests;

public class SuperMultiplexerTests(ITestOutputHelper log)
{
    private readonly ITestOutputHelper _log = log;
    private TextWriter _writer = new TextWriterOutputHelper(log);

    [Fact]
    public async Task Basic()
    {
        var options = ConfigurationOptions.Parse(TestConfig.Current.PrimaryServerAndPort);
        await using var conn = await SuperMultiplexer.ConnectAsync(options, "a", log: _writer);

        options = ConfigurationOptions.Parse(TestConfig.Current.PrimaryServerAndPort);
        await conn.AddAsync(options, "b", log: _writer);

        var db = conn.GetDatabase();
        await db.PingAsync();

        var best = conn.SelectBest(_writer);
        Assert.NotNull(best);
    }
}
