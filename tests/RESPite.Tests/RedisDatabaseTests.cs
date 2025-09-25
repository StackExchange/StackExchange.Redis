using System.Threading.Tasks;
using StackExchange.Redis;
using Xunit;

namespace RESPite.Tests;

public class RedisDatabaseTests(ConnectionFixture fixture, ITestOutputHelper log)
    : IntegrationTestBase(fixture, log)
{
    [Fact]
    public void HashSetGetAll()
    {
        var key = Me();

        using var conn = GetConnection();
        var db = AsDatabase(conn);
        db.HashSet(key, "abc", "xyz");
        db.HashSet(key, "def", "uvw");

        var all = db.HashGetAll(key);
        Assert.Equal(2, all.Length);
        Assert.Contains(new HashEntry("abc", "xyz"), all);
        Assert.Contains(new HashEntry("def", "uvw"), all);
    }

    [Fact]
    public async Task HashSetGetAllAsync()
    {
        var key = Me();

        await using var conn = await GetConnectionAsync();
        var db = AsDatabase(conn);
        await db.HashSetAsync(key, "abc", "xyz");
        await db.HashSetAsync(key, "def", "uvw");

        var all = await db.HashGetAllAsync(key);
        Assert.Equal(2, all.Length);
        Assert.Contains(new HashEntry("abc", "xyz"), all);
        Assert.Contains(new HashEntry("def", "uvw"), all);
    }
}
