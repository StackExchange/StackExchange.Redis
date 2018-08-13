using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    [Collection(NonParallelCollection.Name)]
    public class AggresssiveTests : TestBase
    {
        public AggresssiveTests(ITestOutputHelper output) : base(output) { }


        [Fact]
        public async Task ParallelTransactionsWithConditions()
        {
            const int Muxers = 4, Workers = 20, PerThread = 250;

            var muxers = new ConnectionMultiplexer[Muxers];
            try
            {
                for (int i = 0; i < Muxers; i++)
                    muxers[i] = Create();

                RedisKey hits = Me(), trigger = Me() + "3";
                int expectedSuccess = 0;

                await muxers[0].GetDatabase().KeyDeleteAsync(new[] { hits, trigger });

                Task[] tasks = new Task[Workers];
                for (int i = 0; i < tasks.Length; i++)
                {
                    var scopedDb = muxers[i % Muxers].GetDatabase();
                    var rand = new Random(i);
                    tasks[i] = Task.Run(async () =>
                    {
                        for (int j = 0; j < PerThread; j++)
                        {
                            var oldVal = await scopedDb.StringGetAsync(trigger);
                            var tran = scopedDb.CreateTransaction();
                            tran.AddCondition(Condition.StringEqual(trigger, oldVal));
                            var x = tran.StringIncrementAsync(trigger);
                            var y = tran.StringIncrementAsync(hits);
                            if (await tran.ExecuteAsync())
                            {
                                Interlocked.Increment(ref expectedSuccess);
                                await x;
                                await y;
                            }
                            else
                            {
                                await Assert.ThrowsAsync<TaskCanceledException>(() => x);
                                await Assert.ThrowsAsync<TaskCanceledException>(() => y);
                            }
                        }
                    });
                }
                for (int i = tasks.Length - 1; i >= 0; i--)
                {
                    await tasks[i];
                }
                var actual = (int)await muxers[0].GetDatabase().StringGetAsync(hits);
                Assert.Equal(expectedSuccess, actual);
                Writer.WriteLine($"success: {actual} out of {Workers * PerThread} attempts");
            }
            finally
            {
                for (int i = 0; i < muxers.Length; i++)
                {
                    try { muxers[i]?.Dispose(); } catch { }
                }
            }
        }
    }
}
