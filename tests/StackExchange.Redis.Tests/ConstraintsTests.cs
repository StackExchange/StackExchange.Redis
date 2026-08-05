using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

public class ConstraintsTests(ITestOutputHelper output, SharedConnectionFixture fixture) : TestBase(output, fixture)
{
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
        await using var conn = Create(syncTimeout: 120000); // big timeout while debugging

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
            // Deliberately the long way round: this exercises the optimistic-concurrency path (read, compare,
            // conditional write, observe the abort), which is the thing under test. StringIncrement would be
            // the right answer in real code, and a single compare-and-set write would remove the abort we
            // are here to provoke.
#pragma warning disable SER301 // Transaction can be replaced by a single atomic operation
            tran.AddCondition(Condition.StringEqual(key, oldVal));
            _ = tran.StringSetAsync(key, newVal);
#pragma warning restore SER301
            if (!await tran.ExecuteAsync().ForAwait()) return null; // aborted
            return newVal;
        }
    }
}
