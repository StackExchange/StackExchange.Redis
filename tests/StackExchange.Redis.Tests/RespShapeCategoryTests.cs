using System.Threading.Tasks;
using RESPite.Messages;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Pins the guidance in the docs (Scripting.md, "Testing what came back"): the IsScalar/IsAggregate/IsNull
/// categories hold across protocols, while the specific RespPrefix behind them does not. If a future
/// server or protocol change breaks one of these, the docs are wrong and should change with it.
/// </summary>
[RunPerProtocol]
public class RespShapeCategoryTests(ITestOutputHelper output) : TestBase(output)
{
    [Fact]
    public async Task AggregateCategoryIsStableWhilePrefixIsNot()
    {
        await using var conn = Create();
        var db = conn.GetDatabase();
        bool resp3 = TestContext.Current.IsResp3();

        RedisKey hkey = Me() + ":h", skey = Me() + ":s";
        await db.KeyDeleteAsync(hkey);
        await db.KeyDeleteAsync(skey);
        await db.HashSetAsync(hkey, "f", "v");
        await db.SetAddAsync(skey, "a");

        using (var agg = db.ExecuteResp("HGETALL", new RedisKeyOrValue[] { hkey }))
        {
            var reader = agg.Read();
            Assert.True(reader.IsAggregate, "HGETALL must be IsAggregate under both protocols");
            Assert.Equal(resp3 ? RespPrefix.Map : RespPrefix.Array, reader.Prefix);
        }

        using (var set = db.ExecuteResp("SMEMBERS", new RedisKeyOrValue[] { skey }))
        {
            var reader = set.Read();
            Assert.True(reader.IsAggregate, "SMEMBERS must be IsAggregate under both protocols");
            Assert.Equal(resp3 ? RespPrefix.Set : RespPrefix.Array, reader.Prefix);
        }

        RedisKey zkey = Me() + ":z";
        await db.KeyDeleteAsync(zkey);
        await db.SortedSetAddAsync(zkey, "m", 1.5);
        using (var score = db.ExecuteResp("ZSCORE", new RedisKeyOrValue[] { zkey, (RedisValue)"m" }))
        {
            var reader = score.Read();
            Assert.True(reader.IsScalar, "ZSCORE must be IsScalar under both protocols");
            Assert.Equal(resp3 ? RespPrefix.Double : RespPrefix.BulkString, reader.Prefix);
        }

        using (var missing = db.ExecuteResp("GET", new RedisKeyOrValue[] { (RedisKey)(Me() + ":nope") }))
        {
            var reader = missing.Read();
            Assert.True(reader.IsNull, "a missing GET must be IsNull under both protocols");
            Assert.Equal(resp3 ? RespPrefix.Null : RespPrefix.BulkString, reader.Prefix);
        }
    }
}
