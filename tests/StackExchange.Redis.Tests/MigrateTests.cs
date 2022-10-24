using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests;

public class MigrateTests : TestBase
{
    public MigrateTests(ITestOutputHelper output) : base (output) { }

    [FactLongRunning]
    public async Task Basic()
    {
        var fromConfig = new ConfigurationOptions { EndPoints = { { TestConfig.Current.SecureServer, TestConfig.Current.SecurePort } }, Password = TestConfig.Current.SecurePassword, AllowAdmin = true };
        var toConfig = new ConfigurationOptions { EndPoints = { { TestConfig.Current.PrimaryServer, TestConfig.Current.PrimaryPort } }, AllowAdmin = true };

        using var fromConn = ConnectionMultiplexer.Connect(fromConfig, Writer);
        using var toConn = ConnectionMultiplexer.Connect(toConfig, Writer);

        if (await IsWindows(fromConn) || await IsWindows(toConn))
            Skip.Inconclusive("'migrate' is unreliable on redis-64");

        RedisKey key = Me();
        var fromDb = fromConn.GetDatabase();
        var toDb = toConn.GetDatabase();
        fromDb.KeyDelete(key, CommandFlags.FireAndForget);
        toDb.KeyDelete(key, CommandFlags.FireAndForget);
        fromDb.StringSet(key, "foo", flags: CommandFlags.FireAndForget);
        var dest = toConn.GetEndPoints(true).Single();
        Log("Migrating key...");
        fromDb.KeyMigrate(key, dest, migrateOptions: MigrateOptions.Replace);
        Log("Migration command complete");

        // this is *meant* to be synchronous at the redis level, but
        // we keep seeing it fail on the CI server where the key has *left* the origin, but
        // has *not* yet arrived at the destination; adding a pause while we investigate with
        // the redis folks
        await UntilConditionAsync(TimeSpan.FromSeconds(15), () => !fromDb.KeyExists(key) && toDb.KeyExists(key));

        Assert.False(fromDb.KeyExists(key), "Exists at source");
        Assert.True(toDb.KeyExists(key), "Exists at destination");
        string? s = toDb.StringGet(key);
        Assert.Equal("foo", s);
    }

    private static async Task<bool> IsWindows(ConnectionMultiplexer conn)
    {
        var server = conn.GetServer(conn.GetEndPoints().First());
        var section = (await server.InfoAsync("server")).Single();
        var os = section.FirstOrDefault(
            x => string.Equals("os", x.Key, StringComparison.OrdinalIgnoreCase));
        // note: WSL returns things like "os:Linux 4.4.0-17134-Microsoft x86_64"
        return string.Equals("windows", os.Value, StringComparison.OrdinalIgnoreCase);
    }
}
