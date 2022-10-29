using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests.Issues;

public class SO25113323Tests : TestBase
{
    public SO25113323Tests(ITestOutputHelper output) : base (output) { }

    [Fact]
    public async Task SetExpirationToPassed()
    {
        using var conn = Create();

        // Given
        var key = Me();
        var db = conn.GetDatabase();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        db.HashSet(key, "full", "test", When.NotExists, CommandFlags.PreferMaster);

        await Task.Delay(2000).ForAwait();

        // When
        var serverTime = GetServer(conn).Time();
        var expiresOn = serverTime.AddSeconds(-2);

        var firstResult = db.KeyExpire(key, expiresOn, CommandFlags.PreferMaster);
        var secondResult = db.KeyExpire(key, expiresOn, CommandFlags.PreferMaster);
        var exists = db.KeyExists(key);
        var ttl = db.KeyTimeToLive(key);

        // Then
        Assert.True(firstResult); // could set the first time, but this nukes the key
        Assert.False(secondResult); // can't set, since nuked
        Assert.False(exists); // does not exist since nuked
        Assert.Null(ttl); // no expiry since nuked
    }
}
