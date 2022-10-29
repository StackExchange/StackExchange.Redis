using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests;

[Collection(SharedConnectionFixture.Key)]
public class BatchTests : TestBase
{
    public BatchTests(ITestOutputHelper output, SharedConnectionFixture fixture) : base(output, fixture) { }

    [Fact]
    public void TestBatchNotSent()
    {
        using var conn = Create();
        var db = conn.GetDatabase();
        var key = Me();
        db.KeyDeleteAsync(key);
        db.StringSetAsync(key, "batch-not-sent");
        var batch = db.CreateBatch();

        batch.KeyDeleteAsync(key);
        batch.SetAddAsync(key, "a");
        batch.SetAddAsync(key, "b");
        batch.SetAddAsync(key, "c");

        Assert.Equal("batch-not-sent", db.StringGet(key));
    }

    [Fact]
    public void TestBatchSent()
    {
        using var conn = Create();
        var db = conn.GetDatabase();
        var key = Me();
        db.KeyDeleteAsync(key);
        db.StringSetAsync(key, "batch-sent");
        var tasks = new List<Task>();
        var batch = db.CreateBatch();
        tasks.Add(batch.KeyDeleteAsync(key));
        tasks.Add(batch.SetAddAsync(key, "a"));
        tasks.Add(batch.SetAddAsync(key, "b"));
        tasks.Add(batch.SetAddAsync(key, "c"));
        batch.Execute();

        var result = db.SetMembersAsync(key);
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
