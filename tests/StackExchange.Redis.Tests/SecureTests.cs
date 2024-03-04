using System.Diagnostics;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests;

[Collection(NonParallelCollection.Name)]
public class SecureTests : TestBase
{
    protected override string GetConfiguration() =>
        TestConfig.Current.SecureServerAndPort + ",password=" + TestConfig.Current.SecurePassword + ",name=MyClient";

    public SecureTests(ITestOutputHelper output) : base (output) { }

    [Fact]
    public void MassiveBulkOpsFireAndForgetSecure()
    {
        using var conn = Create();

        RedisKey key = Me();
        var db = conn.GetDatabase();
        db.Ping();

        var watch = Stopwatch.StartNew();

        for (int i = 0; i <= AsyncOpsQty; i++)
        {
            db.StringSet(key, i, flags: CommandFlags.FireAndForget);
        }
        int val = (int)db.StringGet(key);
        Assert.Equal(AsyncOpsQty, val);
        watch.Stop();
        Log("{2}: Time for {0} ops: {1}ms (any order); ops/s: {3}", AsyncOpsQty, watch.ElapsedMilliseconds, Me(),
            AsyncOpsQty / watch.Elapsed.TotalSeconds);
    }

    [Fact]
    public void CheckConfig()
    {
        var config = ConfigurationOptions.Parse(GetConfiguration());
        foreach (var ep in config.EndPoints)
        {
            Log(ep.ToString());
        }
        Assert.Single(config.EndPoints);
        Assert.Equal("changeme", config.Password);
    }

    [Fact]
    public void Connect()
    {
        using var conn = Create();

        conn.GetDatabase().Ping();
    }

    [Theory]
    [InlineData("wrong", "WRONGPASS invalid username-password pair or user is disabled.")]
    [InlineData("", "NOAUTH Returned - connection has not yet authenticated")]
    public async Task ConnectWithWrongPassword(string password, string exepctedMessage)
    {
        using var checkConn = Create();
        var checkServer = GetServer(checkConn);

        var config = ConfigurationOptions.Parse(GetConfiguration());
        config.Password = password;
        config.ConnectRetry = 0; // we don't want to retry on closed sockets in this case.
        config.BacklogPolicy = BacklogPolicy.FailFast;

        var ex = await Assert.ThrowsAsync<RedisConnectionException>(async () =>
        {
            SetExpectedAmbientFailureCount(-1);

            using var conn = await ConnectionMultiplexer.ConnectAsync(config, Writer).ConfigureAwait(false);

            conn.GetDatabase().Ping();
        }).ConfigureAwait(false);
        Log($"Exception ({ex.FailureType}): {ex.Message}");
        Assert.Equal(ConnectionFailureType.AuthenticationFailure, ex.FailureType);
        Assert.StartsWith("It was not possible to connect to the redis server(s). There was an authentication failure; check that passwords (or client certificates) are configured correctly: (RedisServerException) ", ex.Message);

        // This changed in some version...not sure which. For our purposes, splitting on v3 vs v6+
        if (checkServer.Version.IsAtLeast(RedisFeatures.v6_0_0))
        {
            Assert.EndsWith(exepctedMessage, ex.Message);
        }
        else
        {
            Assert.EndsWith("NOAUTH Returned - connection has not yet authenticated", ex.Message);
        }
    }
}
