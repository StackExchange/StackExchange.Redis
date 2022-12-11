using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests.Issues;

public class SO11766033Tests : TestBase
{
    public SO11766033Tests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public void TestNullString()
    {
        using var conn = Create();

        var db = conn.GetDatabase();
        const string? expectedTestValue = null;
        var uid = Me();
        db.StringSetAsync(uid, "abc");
        db.StringSetAsync(uid, expectedTestValue);
        string? testValue = db.StringGet(uid);
        Assert.Null(testValue);
    }

    [Fact]
    public void TestEmptyString()
    {
        using var conn = Create();

        var db = conn.GetDatabase();
        const string expectedTestValue = "";
        var uid = Me();

        db.StringSetAsync(uid, expectedTestValue);
        string? testValue = db.StringGet(uid);

        Assert.Equal(expectedTestValue, testValue);
    }
}
