using System;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests;

public class EnvoyTests : TestBase
{
    public EnvoyTests(ITestOutputHelper output) : base(output) { }

    protected override string GetConfiguration() => TestConfig.Current.ProxyServerAndPort;

    /// <summary>
    /// Tests basic envoy connection with the ability to set and get a key.
    /// </summary>
    [Fact]
    public void TestBasicEnvoyConnection()
    {
        var sb = new StringBuilder();
        Writer.EchoTo(sb);
        try
        {
            using var conn = Create(configuration: GetConfiguration(), keepAlive: 1, connectTimeout: 2000, allowAdmin: true, shared: false, proxy: Proxy.Envoyproxy, log: Writer);

            var db = conn.GetDatabase();

            var key = Me() + "foobar";
            const string value = "barfoo";
            db.StringSet(key, value);

            var expectedVal = db.StringGet(key);

            Assert.Equal(value, expectedVal);
        }
        catch (TimeoutException ex) when (ex.Message == "Connect timeout" || sb.ToString().Contains("Returned, but incorrectly"))
        {
            Skip.Inconclusive("Envoy server not found.");
        }
        catch (AggregateException)
        {
            Skip.Inconclusive("Envoy server not found.");
        }
        catch (RedisConnectionException) when (sb.ToString().Contains("It was not possible to connect to the redis server(s)"))
        {
            Skip.Inconclusive("Envoy server not found.");
        }
    }
}
