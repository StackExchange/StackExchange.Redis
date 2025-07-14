using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

[Collection(SharedConnectionFixture.Key)]
public class BatchTests(ITestOutputHelper output, SharedConnectionFixture fixture) : TestBase(output, fixture)
{
    [Fact]
    public async Task TestBatchNotSent()
    {
        await using var conn = Create();
        var db = conn.GetDatabase();
        var key = Me();
        _ = db.KeyDeleteAsync(key);
        _ = db.StringSetAsync(key, "batch-not-sent");
        var batch = db.CreateBatch();

        _ = batch.KeyDeleteAsync(key);
        _ = batch.SetAddAsync(key, "a");
        _ = batch.SetAddAsync(key, "b");
        _ = batch.SetAddAsync(key, "c");

        Assert.Equal("batch-not-sent", db.StringGet(key));
    }

    [Fact]
    public async Task TestBatchSent()
    {
        await using var conn = Create();
        var db = conn.GetDatabase();
        var key = Me();
        _ = db.KeyDeleteAsync(key);
        _ = db.StringSetAsync(key, "batch-sent");
        var tasks = new List<Task>();
        var batch = db.CreateBatch();
        tasks.Add(batch.KeyDeleteAsync(key));
        tasks.Add(batch.SetAddAsync(key, "a"));
        tasks.Add(batch.SetAddAsync(key, "b"));
        tasks.Add(batch.SetAddAsync(key, "c"));
        batch.Execute();

        var result = db.SetMembersAsync(key);
        tasks.Add(result);
        await Task.WhenAll(tasks.ToArray());

        var arr = result.Result;
        Array.Sort(arr, (x, y) => string.Compare(x, y));
        Assert.Equal(3, arr.Length);
        Assert.Equal("a", arr[0]);
        Assert.Equal("b", arr[1]);
        Assert.Equal("c", arr[2]);
    }
}
