using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

public class LibraryNameSuffixAdminTests(InProcServerFixture fixture)
{
    private ConfigurationOptions NoAdminConfig()
    {
        // start from the shared in-process server config, but explicitly *disable* admin mode
        var options = fixture.Config.Clone();
        options.AllowAdmin = false;
        Assert.False(options.AllowAdmin);
        return options;
    }

    [Fact]
    public async Task AddLibraryNameSuffixWorksWithoutAdmin()
    {
        await using var conn = await ConnectionMultiplexer.ConnectAsync(NoAdminConfig());

        // internally this fixes up connected servers via CLIENT SETINFO (best-effort); the
        // CLIENT SETINFO sub-command is not admin; don't report it (telemetry, etc)
        conn.AddLibraryNameSuffix("mysuffix");
    }

    [Fact]
    public async Task ClientSubCommandsViaExecuteDoNotRequireAdmin()
    {
        await using var conn = await ConnectionMultiplexer.ConnectAsync(NoAdminConfig());
        var server = conn.GetServer(conn.GetEndPoints()[0]);

        // none of these CLIENT sub-commands are admin, so they must not trip the admin-mode guard
        // even though AllowAdmin is disabled (regression: the ad-hoc ExecuteMessage previously did
        // not expose its sub-command to Message.IsAdmin, so CLIENT was treated as wholesale-admin)
        var id = server.Execute("CLIENT", "ID");
        Assert.True((long)id > 0);

        Assert.Equal("OK", (string?)server.Execute("CLIENT", "SETNAME", "roundtrip"));
        Assert.Equal("roundtrip", (string?)server.Execute("CLIENT", "GETNAME"));
    }

    [Fact]
    public async Task AdminClientSubCommandsStillRequireAdmin()
    {
        await using var conn = await ConnectionMultiplexer.ConnectAsync(NoAdminConfig());
        var server = conn.GetServer(conn.GetEndPoints()[0]);

        // CLIENT LIST is a genuine admin sub-command (not in the allow-list), so it must still be
        // blocked when AllowAdmin is disabled - the fix must not blanket-allow every CLIENT usage
        var ex = Assert.Throws<RedisCommandException>(() => server.Execute("CLIENT", "LIST"));
        Assert.Contains("admin mode", ex.Message);
    }
}
