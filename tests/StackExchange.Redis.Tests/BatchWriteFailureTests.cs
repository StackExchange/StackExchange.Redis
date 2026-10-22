using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// A write failure part-way through an <see cref="IBatch"/> poisons the connection; the batch's remaining
/// messages must still complete - faulted, or backlogged and delivered after the reconnect - never be left
/// awaiting a reply that cannot come (#3282).
/// </summary>
public class BatchWriteFailureTests(ITestOutputHelper log)
{
    private sealed class ThrowingMessage(Exception toThrow) : Message(-1, CommandFlags.None, RedisCommand.PING)
    {
        public override int ArgCount => 0;

        protected override void WriteImpl(in MessageWriter writer) => throw toThrow;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EveryBatchCommandCompletesAfterMidBatchWriteFailure(bool queueWhileDisconnected)
    {
        using var server = new InProcessTestServer(log);
        var config = server.GetClientConfig(withPubSub: false, defaultOnly: true);
        config.BacklogPolicy = queueWhileDisconnected ? BacklogPolicy.Default : BacklogPolicy.FailFast;
        config.AbortOnConnectFail = false;
        await using var conn = await ConnectionMultiplexer.ConnectAsync(config);
        var db = conn.GetDatabase();
        RedisKey key = nameof(EveryBatchCommandCompletesAfterMidBatchWriteFailure);
        await db.KeyDeleteAsync(key);

        var batch = (RedisBatch)db.CreateBatch();
        var before = batch.StringIncrementAsync(key);
        var boom = batch.ExecuteAsync(new ThrowingMessage(new InvalidOperationException("simulated WriteImpl failure")), ResultProcessor.DemandOK, false);
        var after1 = batch.StringIncrementAsync(key);
        var after2 = batch.StringIncrementAsync(key);
        batch.Execute();

        Task[] all = [before, boom, after1, after2];
        var everything = Task.WhenAll(all);
        if (await Task.WhenAny(everything, Task.Delay(TimeSpan.FromSeconds(10))) != everything)
        {
            Assert.Fail("hung: " + string.Join(", ", all.Select((t, i) => $"cmd{i}={t.Status}")));
        }
        // individual faults are inspected below

        for (int i = 0; i < all.Length; i++)
        {
            log.WriteLine($"cmd{i}: {all[i].Status} {all[i].Exception?.GetBaseException().GetType().Name} {all[i].Exception?.GetBaseException().Message}");
        }
        Assert.All(all, t => Assert.True(t.IsCompleted));
        Assert.True(boom.IsFaulted);
        // "before" was written and then torn down with the connection: answered first, or failed with it.
        // The tail is backlogged and delivered on the new connection, or refused (cancelled before the write,
        // or faulted at the poisoned connection) - never left pending. Whatever succeeded must be real: the
        // counter reflects every INCR the server ran, which is at least every one reported as successful
        Task<long>[] increments = [before, after1, after2];
        if (queueWhileDisconnected)
        {
            Assert.True(after1.IsCompletedSuccessfully, after1.Status.ToString());
            Assert.True(after2.IsCompletedSuccessfully, after2.Status.ToString());
        }
        else
        {
            Assert.All([after1, after2], t => Assert.True(
                t.IsCompletedSuccessfully || t.IsCanceled || t.Exception?.GetBaseException() is RedisConnectionException, t.Status.ToString()));
        }
        long succeeded = increments.Count(t => t.IsCompletedSuccessfully);
        Assert.InRange((long)await db.StringGetAsync(key), succeeded, increments.Length);
    }
}
