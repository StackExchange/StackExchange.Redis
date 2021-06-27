using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    [Collection(SharedConnectionFixture.Key)]
    public class Batches : TestBase
    {
        public Batches(ITestOutputHelper output, SharedConnectionFixture fixture) : base(output, fixture) { }

        [Fact]
        public void TestBatchNotSent()
        {
            using (var muxer = Create())
            {
                var conn = muxer.GetDatabase();
                var key = Me();
                conn.KeyDeleteAsync(key);
                conn.StringSetAsync(key, "batch-not-sent");
                var batch = conn.CreateBatch();

                batch.KeyDeleteAsync(key);
                batch.SetAddAsync(key, "a");
                batch.SetAddAsync(key, "b");
                batch.SetAddAsync(key, "c");

                Assert.Equal("batch-not-sent", conn.StringGet(key));
            }
        }

        [Fact]
        public void TestBatchSent()
        {
            using (var muxer = Create())
            {
                var conn = muxer.GetDatabase();
                var key = Me();
                conn.KeyDeleteAsync(key);
                conn.StringSetAsync(key, "batch-sent");
                var tasks = new List<Task>();
                var batch = conn.CreateBatch();
                tasks.Add(batch.KeyDeleteAsync(key));
                tasks.Add(batch.SetAddAsync(key, "a"));
                tasks.Add(batch.SetAddAsync(key, "b"));
                tasks.Add(batch.SetAddAsync(key, "c"));
                batch.Execute();

                var result = conn.SetMembersAsync(key);
                tasks.Add(result);
                Task.WhenAll(tasks.ToArray());

                var arr = result.Result;
                Array.Sort(arr, (x, y) => string.Compare(x, y));
                Assert.Equal(3, arr.Length);
                Assert.Equal("a", arr[0]);
                Assert.Equal("b", arr[1]);
                Assert.Equal("c", arr[2]);
            }
        }
    }
}
