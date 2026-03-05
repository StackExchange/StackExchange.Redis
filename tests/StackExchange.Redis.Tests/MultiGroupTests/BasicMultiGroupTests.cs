using System.IO;
using System.Threading.Tasks;
using StackExchange.Redis.Tests.Helpers;
using Xunit;

namespace StackExchange.Redis.Tests.MultiGroupTests;

public class BasicMultiGroupTests(ITestOutputHelper log)
{
    public TextWriter Log { get; } = new TextWriterOutputHelper(log);

    [Fact]
    public async Task BasicSmokeTest()
    {
        using var server0 = new InProcessTestServer(log);
        using var server1 = new InProcessTestServer(log);

        ConfigurationOptions[] configs = [server0.GetClientConfig(), server1.GetClientConfig()];
        await using var conn = await MultiGroupMultiplexer.ConnectAsync(configs, Log);
        Assert.True(conn.IsConnected);
    }
}
