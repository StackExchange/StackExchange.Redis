using System;
using System.Diagnostics;
using NUnit.Framework;
using StackExchange.Redis;

namespace Tests.Issues
{
    [TestFixture]
    public class SO10504853
    {
        [Test]
        public void LoopLotsOfTrivialStuff()
        {
            Trace.WriteLine("### init");
            using (var muxer = Config.GetUnsecuredConnection())
            {
                var conn = muxer.GetDatabase(0);
                conn.KeyDelete("lots-trivial");
            }
            const int COUNT = 2;
            for (int i = 0; i < COUNT; i++)
            {
                Trace.WriteLine("### incr:" + i);
                using (var muxer = Config.GetUnsecuredConnection())
                {
                    var conn = muxer.GetDatabase(0);
                    Assert.AreEqual(i + 1, conn.StringIncrement("lots-trivial"));
                }
            }
            Trace.WriteLine("### close");
            using (var muxer = Config.GetUnsecuredConnection())
            {
                var conn = muxer.GetDatabase(0);
                Assert.AreEqual(COUNT, (long)conn.StringGet("lots-trivial"));
            }
        }
        [Test]
        public void ExecuteWithEmptyStartingPoint()
        {
            using (var muxer = Config.GetUnsecuredConnection())
            {
                var conn = muxer.GetDatabase(0);
                var task = new { priority = 3 };
                conn.KeyDeleteAsync("item:1");
                conn.HashSetAsync("item:1", "something else", "abc");
                conn.HashSetAsync("item:1", "priority", task.priority.ToString());

                var taskResult = conn.HashGetAsync("item:1", "priority");

                conn.Wait(taskResult);

                var priority = Int32.Parse(taskResult.Result);

                Assert.AreEqual(3, priority);
            }
        }

        [Test]
        public void ExecuteWithNonHashStartingPoint()
        {
            Assert.Throws<RedisConnectionException>(() =>
            {
                using (var muxer = Config.GetUnsecuredConnection())
                {
                    var conn = muxer.GetDatabase(0);
                    var task = new { priority = 3 };
                    conn.KeyDeleteAsync("item:1");
                    conn.StringSetAsync("item:1", "not a hash");
                    conn.HashSetAsync("item:1", "priority", task.priority.ToString());

                    var taskResult = conn.HashGetAsync("item:1", "priority");

                    try
                    {
                        conn.Wait(taskResult);
                        Assert.Fail();
                    }
                    catch (AggregateException ex)
                    {
                        throw ex.InnerExceptions[0];
                    }
                }
            },
            message: "WRONGTYPE Operation against a key holding the wrong kind of value");
        }
    }
}
