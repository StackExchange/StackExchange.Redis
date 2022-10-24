using System;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests.Issues;

public class SO10825542Tests : TestBase
{
    public SO10825542Tests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public async Task Execute()
    {
        using var conn = Create();
        var key = Me();

        var db = conn.GetDatabase();
        // set the field value and expiration
        _ = db.HashSetAsync(key, "field1", Encoding.UTF8.GetBytes("hello world"));
        _ = db.KeyExpireAsync(key, TimeSpan.FromSeconds(7200));
        _ = db.HashSetAsync(key, "field2", "fooobar");
        var result = await db.HashGetAllAsync(key).ForAwait();

        Assert.Equal(2, result.Length);
        var dict = result.ToStringDictionary();
        Assert.Equal("hello world", dict["field1"]);
        Assert.Equal("fooobar", dict["field2"]);
    }
}
