using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests.Issues;

public class Issue10Tests(ITestOutputHelper output) : TestBase(output)
{
    [Fact]
    public async Task Execute()
    {
        await using var conn = Create();

        var key = Me();
        var db = conn.GetDatabase();
        _ = db.KeyDeleteAsync(key); // contents: nil
        _ = db.ListLeftPushAsync(key, "abc"); // "abc"
        _ = db.ListLeftPushAsync(key, "def"); // "def", "abc"
        _ = db.ListLeftPushAsync(key, "ghi"); // "ghi", "def", "abc",
        _ = db.ListSetByIndexAsync(key, 1, "jkl"); // "ghi", "jkl", "abc"

        var contents = await db.ListRangeAsync(key, 0, -1);
        Assert.Equal(3, contents.Length);
        Assert.Equal("ghi", contents[0]);
        Assert.Equal("jkl", contents[1]);
        Assert.Equal("abc", contents[2]);
    }
}
