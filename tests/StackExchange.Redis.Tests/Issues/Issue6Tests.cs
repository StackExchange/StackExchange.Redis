using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests.Issues;

public class Issue6Tests(ITestOutputHelper output) : TestBase(output)
{
    [Fact]
    public async Task ShouldWorkWithoutEchoOrPing()
    {
        await using var conn = Create(proxy: Proxy.Twemproxy);

        Log("config: " + conn.Configuration);
        var db = conn.GetDatabase();
        var time = await db.PingAsync();
        Log("ping time: " + time);
    }
}
