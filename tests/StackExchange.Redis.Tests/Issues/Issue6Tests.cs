using Xunit.Abstractions;

namespace StackExchange.Redis.Tests.Issues;

public class Issue6Tests :  TestBase
{
    public Issue6Tests(ITestOutputHelper output) : base (output) { }

    [Fact]
    public void ShouldWorkWithoutEchoOrPing()
    {
        using var conn = Create(proxy: Proxy.Twemproxy);

        Log("config: " + conn.Configuration);
        var db = conn.GetDatabase();
        var time = db.Ping();
        Log("ping time: " + time);
    }
}
