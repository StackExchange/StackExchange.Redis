using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests.Issues;

public class SO11766033Tests(ITestOutputHelper output) : TestBase(output)
{
    [Fact]
    public async Task TestNullString()
    {
        await using var conn = Create();

        var db = conn.GetDatabase();
        const string? expectedTestValue = null;
        var uid = Me();
        _ = db.StringSetAsync(uid, "abc");
        _ = db.StringSetAsync(uid, expectedTestValue);
        string? testValue = db.StringGet(uid);
        Assert.Null(testValue);
    }

    [Fact]
    public async Task TestEmptyString()
    {
        await using var conn = Create();

        var db = conn.GetDatabase();
        const string expectedTestValue = "";
        var uid = Me();

        _ = db.StringSetAsync(uid, expectedTestValue);
        string? testValue = db.StringGet(uid);

        Assert.Equal(expectedTestValue, testValue);
    }
}
