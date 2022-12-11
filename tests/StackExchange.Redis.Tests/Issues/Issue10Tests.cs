using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests.Issues;

public class Issue10Tests : TestBase
{
    public Issue10Tests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public void Execute()
    {
        using var conn = Create();

        var key = Me();
        var db = conn.GetDatabase();
        db.KeyDeleteAsync(key); // contents: nil
        db.ListLeftPushAsync(key, "abc"); // "abc"
        db.ListLeftPushAsync(key, "def"); // "def", "abc"
        db.ListLeftPushAsync(key, "ghi"); // "ghi", "def", "abc",
        db.ListSetByIndexAsync(key, 1, "jkl"); // "ghi", "jkl", "abc"

        var contents = db.Wait(db.ListRangeAsync(key, 0, -1));
        Assert.Equal(3, contents.Length);
        Assert.Equal("ghi", contents[0]);
        Assert.Equal("jkl", contents[1]);
        Assert.Equal("abc", contents[2]);
    }
}
