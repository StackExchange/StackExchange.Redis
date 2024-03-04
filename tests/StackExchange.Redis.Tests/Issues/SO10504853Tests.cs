using System;
using System.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests.Issues;

public class SO10504853Tests : TestBase
{
    public SO10504853Tests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public void LoopLotsOfTrivialStuff()
    {
        var key = Me();
        Trace.WriteLine("### init");
        using (var conn = Create())
        {
            var db = conn.GetDatabase();
            db.KeyDelete(key, CommandFlags.FireAndForget);
        }
        const int COUNT = 2;
        for (int i = 0; i < COUNT; i++)
        {
            Trace.WriteLine("### incr:" + i);
            using var conn = Create();
            var db = conn.GetDatabase();
            Assert.Equal(i + 1, db.StringIncrement(key));
        }
        Trace.WriteLine("### close");
        using (var conn = Create())
        {
            var db = conn.GetDatabase();
            Assert.Equal(COUNT, (long)db.StringGet(key));
        }
    }

    [Fact]
    public void ExecuteWithEmptyStartingPoint()
    {
        using var conn = Create();

        var db = conn.GetDatabase();
        var key = Me();
        var task = new { priority = 3 };
        db.KeyDeleteAsync(key);
        db.HashSetAsync(key, "something else", "abc");
        db.HashSetAsync(key, "priority", task.priority.ToString());

        var taskResult = db.HashGetAsync(key, "priority");

        db.Wait(taskResult);

        var priority = int.Parse(taskResult.Result!);

        Assert.Equal(3, priority);
    }

    [Fact]
    public void ExecuteWithNonHashStartingPoint()
    {
        var key = Me();
        Assert.Throws<RedisServerException>(() =>
        {
            using var conn = Create();

            var db = conn.GetDatabase();
            var task = new { priority = 3 };
            db.KeyDeleteAsync(key);
            db.StringSetAsync(key, "not a hash");
            db.HashSetAsync(key, "priority", task.priority.ToString());

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
