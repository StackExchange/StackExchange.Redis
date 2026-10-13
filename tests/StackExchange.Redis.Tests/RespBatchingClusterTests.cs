using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>A batch in a cluster: one run per slot, each to the node that owns it.</summary>
public class RespBatchingClusterTests(ITestOutputHelper output) : TestBase(output)
{
    protected override string GetConfiguration() => TestConfig.Current.ClusterServersAndPorts + ",connectTimeout=10000";

    /// <summary>
    /// Keys in several slots, and so on several nodes, composed into one batch: every command completes, with its
    /// own answer, and the writes really happened where the keys live.
    /// </summary>
    [Fact]
    public async Task ABatchSpanningSlotsCompletesEveryCommand()
    {
        await using var conn = Create();
        var db = conn.GetDatabase();
        var prefix = Me();
        RedisKey[] keys = [$"{{a}}:{prefix}", $"{{b}}:{prefix}", $"{{c}}:{prefix}", $"{{d}}:{prefix}"];
        Assert.True(conn.HashSlot(keys[0]) != conn.HashSlot(keys[1]), "the keys should not share a slot");
        foreach (var key in keys) await db.KeyDeleteAsync(key);

        using var batch = db.BeginBatch();
        var sets = new ValueTask<bool>[keys.Length];
        var gets = new ValueTask<RedisValue>[keys.Length];
        for (var i = 0; i < keys.Length; i++)
        {
            sets[i] = batch.Strings.SetAsync(keys[i], "v" + i);
            gets[i] = batch.Strings.GetAsync(keys[i]);
        }

        await batch.ExecuteAsync();

        for (var i = 0; i < keys.Length; i++)
        {
            Assert.True(await sets[i]);
            Assert.Equal("v" + i, (string?)await gets[i]); // within a slot, the run keeps its order
            Assert.Equal("v" + i, (string?)await db.StringGetAsync(keys[i]));
        }
    }
}
