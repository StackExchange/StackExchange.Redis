using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests;

[Collection(SharedConnectionFixture.Key)]
public class ConstraintsTests : TestBase
{
    public ConstraintsTests(ITestOutputHelper output, SharedConnectionFixture fixture) : base(output, fixture) { }

    [Fact]
    public void ValueEquals()
    {
        RedisValue x = 1, y = "1";
        Assert.True(x.Equals(y), "equals");
        Assert.True(x == y, "operator");
    }

    [Fact]
    public async Task TestManualIncr()
    {
        using var conn = Create(syncTimeout: 120000); // big timeout while debugging

        var key = Me();
        var db = conn.GetDatabase();
        for (int i = 0; i < 10; i++)
        {
            db.KeyDelete(key, CommandFlags.FireAndForget);
            Assert.Equal(1, await ManualIncrAsync(db, key).ForAwait());
            Assert.Equal(2, await ManualIncrAsync(db, key).ForAwait());
            Assert.Equal(2, (long)db.StringGet(key));
        }
    }

    public static async Task<long?> ManualIncrAsync(IDatabase connection, RedisKey key)
    {
        var oldVal = (long?)await connection.StringGetAsync(key).ForAwait();
        var newVal = (oldVal ?? 0) + 1;
        var tran = connection.CreateTransaction();
        { // check hasn't changed
            tran.AddCondition(Condition.StringEqual(key, oldVal));
            _ = tran.StringSetAsync(key, newVal);
            if (!await tran.ExecuteAsync().ForAwait()) return null; // aborted
            return newVal;
        }
    }
}
