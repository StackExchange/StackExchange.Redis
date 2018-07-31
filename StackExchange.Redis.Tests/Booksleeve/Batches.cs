using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests.Booksleeve
{
    public class Batches : BookSleeveTestBase
    {
        public Batches(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void TestBatchNotSent()
        {
            using (var muxer = GetUnsecuredConnection())
            {
                var conn = muxer.GetDatabase(0);
                conn.KeyDeleteAsync("batch");
                conn.StringSetAsync("batch", "batch-not-sent");
                var tasks = new List<Task>();
                var batch = conn.CreateBatch();

                tasks.Add(batch.KeyDeleteAsync("batch"));
                tasks.Add(batch.SetAddAsync("batch", "a"));
                tasks.Add(batch.SetAddAsync("batch", "b"));
                tasks.Add(batch.SetAddAsync("batch", "c"));

                Assert.Equal("batch-not-sent", conn.StringGet("batch"));
            }
        }

        [Fact]
        public void TestBatchSent()
        {
            using (var muxer = GetUnsecuredConnection())
            {
                var conn = muxer.GetDatabase(0);
                conn.KeyDeleteAsync("batch");
                conn.StringSetAsync("batch", "batch-sent");
                var tasks = new List<Task>();
                var batch = conn.CreateBatch();
                tasks.Add(batch.KeyDeleteAsync("batch"));
                tasks.Add(batch.SetAddAsync("batch", "a"));
                tasks.Add(batch.SetAddAsync("batch", "b"));
                tasks.Add(batch.SetAddAsync("batch", "c"));
                batch.Execute();

                var result = conn.SetMembersAsync("batch");
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
