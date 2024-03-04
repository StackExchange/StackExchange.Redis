using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests;

[Collection(NonParallelCollection.Name)]
public class AsyncTests : TestBase
{
    public AsyncTests(ITestOutputHelper output) : base(output) { }

    protected override string GetConfiguration() => TestConfig.Current.PrimaryServerAndPort;

    [Fact]
    public void AsyncTasksReportFailureIfServerUnavailable()
    {
        SetExpectedAmbientFailureCount(-1); // this will get messy

        using var conn = Create(allowAdmin: true, shared: false, backlogPolicy: BacklogPolicy.FailFast);
        var server = conn.GetServer(TestConfig.Current.PrimaryServer, TestConfig.Current.PrimaryPort);

        RedisKey key = Me();
        var db = conn.GetDatabase();
        db.KeyDelete(key);
        var a = db.SetAddAsync(key, "a");
        var b = db.SetAddAsync(key, "b");

        Assert.True(conn.Wait(a));
        Assert.True(conn.Wait(b));

        conn.AllowConnect = false;
        server.SimulateConnectionFailure(SimulatedFailureType.All);
        var c = db.SetAddAsync(key, "c");

        Assert.True(c.IsFaulted, "faulted");
        Assert.NotNull(c.Exception);
        var ex = c.Exception.InnerExceptions.Single();
        Assert.IsType<RedisConnectionException>(ex);
        Assert.StartsWith("No connection is active/available to service this operation: SADD " + key.ToString(), ex.Message);
    }

    [Fact]
    public async Task AsyncTimeoutIsNoticed()
    {
        using var conn = Create(syncTimeout: 1000, asyncTimeout: 1000);
        using var pauseConn = Create();
        var opt = ConfigurationOptions.Parse(conn.Configuration);
        if (!Debugger.IsAttached)
        { // we max the timeouts if a debugger is detected
            Assert.Equal(1000, opt.AsyncTimeout);
        }

        RedisKey key = Me();
        var val = Guid.NewGuid().ToString();
        var db = conn.GetDatabase();
        db.StringSet(key, val);

        Assert.Contains("; async timeouts: 0;", conn.GetStatus());

        // This is done on another connection, because it queues a SELECT due to being an unknown command that will not timeout
        // at the head of the queue
        await pauseConn.GetDatabase().ExecuteAsync("client", "pause", 4000).ForAwait(); // client pause returns immediately

        var ms = Stopwatch.StartNew();
        var ex = await Assert.ThrowsAsync<RedisTimeoutException>(async () =>
        {
            Log("Issuing StringGetAsync");
            await db.StringGetAsync(key).ForAwait(); // but *subsequent* operations are paused
            ms.Stop();
            Log($"Unexpectedly succeeded after {ms.ElapsedMilliseconds}ms");
        }).ForAwait();
        ms.Stop();
        Log($"Timed out after {ms.ElapsedMilliseconds}ms");

        Log("Exception message: " + ex.Message);
        Assert.Contains("Timeout awaiting response", ex.Message);
        // Ensure we are including the last payload size
        Assert.Contains("last-in:", ex.Message);
        Assert.DoesNotContain("last-in: 0", ex.Message);
        Assert.NotNull(ex.Data["Redis-Last-Result-Bytes"]);

        Assert.Contains("cur-in:", ex.Message);

        string status = conn.GetStatus();
        Log(status);
        Assert.Contains("; async timeouts: 1;", status);
    }
}
