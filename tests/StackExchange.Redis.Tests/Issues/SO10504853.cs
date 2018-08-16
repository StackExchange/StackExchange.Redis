using System;
using System.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests.Issues
{
    public class SO10504853 : TestBase
    {
        public SO10504853(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void LoopLotsOfTrivialStuff()
        {
            var key = Me();
            Trace.WriteLine("### init");
            using (var muxer = Create())
            {
                var conn = muxer.GetDatabase();
                conn.KeyDelete(key, CommandFlags.FireAndForget);
            }
            const int COUNT = 2;
            for (int i = 0; i < COUNT; i++)
            {
                Trace.WriteLine("### incr:" + i);
                using (var muxer = Create())
                {
                    var conn = muxer.GetDatabase();
                    Assert.Equal(i + 1, conn.StringIncrement(key));
                }
            }
            Trace.WriteLine("### close");
            using (var muxer = Create())
            {
                var conn = muxer.GetDatabase();
                Assert.Equal(COUNT, (long)conn.StringGet(key));
            }
        }

        [Fact]
        public void ExecuteWithEmptyStartingPoint()
        {
            using (var muxer = Create())
            {
                var conn = muxer.GetDatabase();
                var key = Me();
                var task = new { priority = 3 };
                conn.KeyDeleteAsync(key);
                conn.HashSetAsync(key, "something else", "abc");
                conn.HashSetAsync(key, "priority", task.priority.ToString());

                var taskResult = conn.HashGetAsync(key, "priority");

                conn.Wait(taskResult);

                var priority = int.Parse(taskResult.Result);

                Assert.Equal(3, priority);
            }
        }

        [Fact]
        public void ExecuteWithNonHashStartingPoint()
        {
            var key = Me();
            Assert.Throws<RedisServerException>(() =>
            {
                using (var muxer = Create())
                {
                    var conn = muxer.GetDatabase();
                    var task = new { priority = 3 };
                    conn.KeyDeleteAsync(key);
                    conn.StringSetAsync(key, "not a hash");
                    conn.HashSetAsync(key, "priority", task.priority.ToString());

                    var taskResult = conn.HashGetAsync(key, "priority");

                    try
                    {
                        conn.Wait(taskResult);
                        Assert.True(false, "Should throw a WRONGTYPE");
                    }
                    catch (AggregateException ex)
                    {
                        throw ex.InnerExceptions[0];
                    }
                }
            }); // WRONGTYPE Operation against a key holding the wrong kind of value
        }
    }
}
