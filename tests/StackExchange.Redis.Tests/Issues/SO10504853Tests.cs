using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests.Issues;

public class SO10504853Tests(ITestOutputHelper output) : TestBase(output)
{
    [Fact]
    public async Task LoopLotsOfTrivialStuff()
    {
        var key = Me();
        Trace.WriteLine("### init");
        await using (var conn = Create())
        {
            var db = conn.GetDatabase();
            db.KeyDelete(key, CommandFlags.FireAndForget);
        }
        const int COUNT = 2;
        for (int i = 0; i < COUNT; i++)
        {
            Trace.WriteLine("### incr:" + i);
            await using var conn = Create();
            var db = conn.GetDatabase();
            Assert.Equal(i + 1, db.StringIncrement(key));
        }
        Trace.WriteLine("### close");
        await using (var conn = Create())
        {
            var db = conn.GetDatabase();
            Assert.Equal(COUNT, (long)db.StringGet(key));
        }
    }

    [Fact]
    public async Task ExecuteWithEmptyStartingPoint()
    {
        await using var conn = Create();

        var db = conn.GetDatabase();
        var key = Me();
        var task = new { priority = 3 };
        _ = db.KeyDeleteAsync(key);
        _ = db.HashSetAsync(key, "something else", "abc");
        _ = db.HashSetAsync(key, "priority", task.priority.ToString());

        var taskResult = db.HashGetAsync(key, "priority");

        await taskResult;

        var priority = int.Parse(taskResult.Result!);

        Assert.Equal(3, priority);
    }

    [Fact]
    public async Task ExecuteWithNonHashStartingPoint()
    {
        var key = Me();
        await Assert.ThrowsAsync<RedisServerException>(async () =>
        {
            await using var conn = Create();

            var db = conn.GetDatabase();
            var task = new { priority = 3 };
            _ = db.KeyDeleteAsync(key);
            _ = db.StringSetAsync(key, "not a hash");
            _ = db.HashSetAsync(key, "priority", task.priority.ToString());

            var taskResult = db.HashGetAsync(key, "priority");

            try
            {
                db.Wait(taskResult);
                Assert.Fail("Should throw a WRONGTYPE");
            }
            catch (AggregateException ex)
            {
                throw ex.InnerExceptions[0];
            }
        }); // WRONGTYPE Operation against a key holding the wrong kind of value
    }
}
