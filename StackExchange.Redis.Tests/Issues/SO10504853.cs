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
            Trace.WriteLine("### init");
            using (var muxer = Create())
            {
                var conn = muxer.GetDatabase();
                conn.KeyDelete("lots-trivial");
            }
            const int COUNT = 2;
            for (int i = 0; i < COUNT; i++)
            {
                Trace.WriteLine("### incr:" + i);
                using (var muxer = Create())
                {
                    var conn = muxer.GetDatabase();
                    Assert.Equal(i + 1, conn.StringIncrement("lots-trivial"));
                }
            }
            Trace.WriteLine("### close");
            using (var muxer = Create())
            {
                var conn = muxer.GetDatabase();
                Assert.Equal(COUNT, (long)conn.StringGet("lots-trivial"));
            }
        }

        [Fact]
        public void ExecuteWithEmptyStartingPoint()
        {
            using (var muxer = Create())
            {
                var conn = muxer.GetDatabase();
                var task = new { priority = 3 };
                conn.KeyDeleteAsync("item:1");
                conn.HashSetAsync("item:1", "something else", "abc");
                conn.HashSetAsync("item:1", "priority", task.priority.ToString());

                var taskResult = conn.HashGetAsync("item:1", "priority");

                conn.Wait(taskResult);

                var priority = int.Parse(taskResult.Result);

                Assert.Equal(3, priority);
            }
        }

        [Fact]
        public void ExecuteWithNonHashStartingPoint()
        {
            Assert.Throws<RedisServerException>(() =>
            {
                using (var muxer = Create())
                {
                    var conn = muxer.GetDatabase();
                    var task = new { priority = 3 };
                    conn.KeyDeleteAsync("item:1");
                    conn.StringSetAsync("item:1", "not a hash");
                    conn.HashSetAsync("item:1", "priority", task.priority.ToString());

                    var taskResult = conn.HashGetAsync("item:1", "priority");

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
