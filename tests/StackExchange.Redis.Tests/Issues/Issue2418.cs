using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests.Issues;

public class Issue2418 : TestBase
{
    public Issue2418(ITestOutputHelper output, SharedConnectionFixture? fixture = null)
        : base(output, fixture) { }

    [Fact]
    public async Task Execute()
    {
        using var conn = Create();
        var db = conn.GetDatabase();

        RedisKey key = Me();
        RedisValue someInt = 12;
        Assert.False(someInt.IsNullOrEmpty, nameof(someInt.IsNullOrEmpty) + " before");
        Assert.True(someInt.IsInteger, nameof(someInt.IsInteger) + " before");
        await db.HashSetAsync(key, new[]
        {
            new HashEntry("some_int", someInt),
            // ...
        });

        // check we can fetch it
        var entry = await db.HashGetAllAsync(key);
        Assert.NotEmpty(entry);
        Assert.Single(entry);
        foreach (var pair in entry)
        {
            Log($"'{pair.Name}'='{pair.Value}'");
        }


        // filter with LINQ
        Assert.True(entry.Any(x => x.Name == "some_int"), "Any");
        someInt = entry.FirstOrDefault(x => x.Name == "some_int").Value;
        Log($"found via Any: '{someInt}'");
        Assert.False(someInt.IsNullOrEmpty, nameof(someInt.IsNullOrEmpty) + " after");
        Assert.True(someInt.TryParse(out int i));
        Assert.Equal(12, i);
        Assert.Equal(12, (int)someInt);
    }
}
