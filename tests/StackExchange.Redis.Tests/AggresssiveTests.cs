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

        [FactLongRunning]
        public async Task ParallelTransactionsWithConditions()
        {
            const int Muxers = 4, Workers = 20, PerThread = 250;

            var muxers = new IConnectionMultiplexer[Muxers];
            try
            {
                for (int i = 0; i < Muxers; i++)
                    muxers[i] = Create();

                RedisKey hits = Me(), trigger = Me() + "3";
                int expectedSuccess = 0;

                await muxers[0].GetDatabase().KeyDeleteAsync(new[] { hits, trigger }).ForAwait();

                Task[] tasks = new Task[Workers];
                for (int i = 0; i < tasks.Length; i++)
                {
                    var scopedDb = muxers[i % Muxers].GetDatabase();
                    tasks[i] = Task.Run(async () =>
                    {
                        for (int j = 0; j < PerThread; j++)
                        {
                            var oldVal = await scopedDb.StringGetAsync(trigger).ForAwait();
                            var tran = scopedDb.CreateTransaction();
                            tran.AddCondition(Condition.StringEqual(trigger, oldVal));
                            var x = tran.StringIncrementAsync(trigger);
                            var y = tran.StringIncrementAsync(hits);
                            if (await tran.ExecuteAsync().ForAwait())
                            {
                                Interlocked.Increment(ref expectedSuccess);
                                await x;
                                await y;
                            }
                            else
                            {
                                await Assert.ThrowsAsync<TaskCanceledException>(() => x).ForAwait();
                                await Assert.ThrowsAsync<TaskCanceledException>(() => y).ForAwait();
                            }
                        }
                    });
                }
                for (int i = tasks.Length - 1; i >= 0; i--)
                {
                    await tasks[i];
                }
                var actual = (int)await muxers[0].GetDatabase().StringGetAsync(hits).ForAwait();
                Assert.Equal(expectedSuccess, actual);
                Writer.WriteLine($"success: {actual} out of {Workers * PerThread} attempts");
            }
            finally
            {
                for (int i = 0; i < muxers.Length; i++)
                {
                    try { muxers[i]?.Dispose(); }
                    catch { /* Don't care */ }
                }
            }
        }

        private const int IterationCount = 5000, InnerCount = 20;

        [FactLongRunning]
        public void RunCompetingBatchesOnSameMuxer()
        {
            using (var muxer = Create())
            {
                var db = muxer.GetDatabase();

                Thread x = new Thread(state => BatchRunPings((IDatabase)state))
                {
                    Name = nameof(BatchRunPings)
                };
                Thread y = new Thread(state => BatchRunIntegers((IDatabase)state))
                {
                    Name = nameof(BatchRunIntegers)
                };

                x.Start(db);
                y.Start(db);
                x.Join();
                y.Join();

                Writer.WriteLine(muxer.GetCounters().Interactive);
            }
        }

        private void BatchRunIntegers(IDatabase db)
        {
            var key = Me();
            db.KeyDelete(key);
            db.StringSet(key, 1);
            Task[] tasks = new Task[InnerCount];
            for(int i = 0; i < IterationCount; i++)
            {
                var batch = db.CreateBatch();
                for (int j = 0; j < tasks.Length; j++)
                {
                    tasks[j] = batch.StringIncrementAsync(key);
                }
                batch.Execute();
                db.Multiplexer.WaitAll(tasks);
            }

            var count = (long)db.StringGet(key);
            Writer.WriteLine($"tally: {count}");
        }

        private void BatchRunPings(IDatabase db)
        {
            Task[] tasks = new Task[InnerCount];
            for (int i = 0; i < IterationCount; i++)
            {
                var batch = db.CreateBatch();
                for (int j = 0; j < tasks.Length; j++)
                {
                    tasks[j] = batch.PingAsync();
                }
                batch.Execute();
                db.Multiplexer.WaitAll(tasks);
            }
        }

        [FactLongRunning]
        public async Task RunCompetingBatchesOnSameMuxerAsync()
        {
            using (var muxer = Create())
            {
                var db = muxer.GetDatabase();

                var x = Task.Run(() => BatchRunPingsAsync(db));
                var y = Task.Run(() => BatchRunIntegersAsync(db));

                await x;
                await y;

                Writer.WriteLine(muxer.GetCounters().Interactive);
            }
        }

        private async Task BatchRunIntegersAsync(IDatabase db)
        {
            var key = Me();
            await db.KeyDeleteAsync(key).ForAwait();
            await db.StringSetAsync(key, 1).ForAwait();
            Task[] tasks = new Task[InnerCount];
            for (int i = 0; i < IterationCount; i++)
            {
                var batch = db.CreateBatch();
                for (int j = 0; j < tasks.Length; j++)
                {
                    tasks[j] = batch.StringIncrementAsync(key);
                }
                batch.Execute();
                for(int j = tasks.Length - 1; j >= 0;j--)
                {
                    await tasks[j];
                }
            }

            var count = (long)await db.StringGetAsync(key).ForAwait();
            Writer.WriteLine($"tally: {count}");
        }

        private async Task BatchRunPingsAsync(IDatabase db)
        {
            Task[] tasks = new Task[InnerCount];
            for (int i = 0; i < IterationCount; i++)
            {
                var batch = db.CreateBatch();
                for (int j = 0; j < tasks.Length; j++)
                {
                    tasks[j] = batch.PingAsync();
                }
                batch.Execute();
                for (int j = tasks.Length - 1; j >= 0; j--)
                {
                    await tasks[j];
                }
            }
        }

        [FactLongRunning]
        public void RunCompetingTransactionsOnSameMuxer()
        {
            using (var muxer = Create(logTransactionData: false))
            {
                var db = muxer.GetDatabase();

                Thread x = new Thread(state => TranRunPings((IDatabase)state))
                {
                    Name = nameof(BatchRunPings)
                };
                Thread y = new Thread(state => TranRunIntegers((IDatabase)state))
                {
                    Name = nameof(BatchRunIntegers)
                };

                x.Start(db);
                y.Start(db);
                x.Join();
                y.Join();

                Writer.WriteLine(muxer.GetCounters().Interactive);
            }
        }

        private void TranRunIntegers(IDatabase db)
        {
            var key = Me();
            db.KeyDelete(key);
            db.StringSet(key, 1);
            Task[] tasks = new Task[InnerCount];
            for (int i = 0; i < IterationCount; i++)
            {
                var batch = db.CreateTransaction();
                batch.AddCondition(Condition.KeyExists(key));
                for (int j = 0; j < tasks.Length; j++)
                {
                    tasks[j] = batch.StringIncrementAsync(key);
                }
                batch.Execute();
                db.Multiplexer.WaitAll(tasks);
            }

            var count = (long)db.StringGet(key);
            Writer.WriteLine($"tally: {count}");
        }

        private void TranRunPings(IDatabase db)
        {
            var key = Me();
            db.KeyDelete(key);
            Task[] tasks = new Task[InnerCount];
            for (int i = 0; i < IterationCount; i++)
            {
                var batch = db.CreateTransaction();
                batch.AddCondition(Condition.KeyNotExists(key));
                for (int j = 0; j < tasks.Length; j++)
                {
                    tasks[j] = batch.PingAsync();
                }
                batch.Execute();
                db.Multiplexer.WaitAll(tasks);
            }
        }

        [FactLongRunning]
        public async Task RunCompetingTransactionsOnSameMuxerAsync()
        {
            using (var muxer = Create(logTransactionData: false))
            {
                var db = muxer.GetDatabase();

                var x = Task.Run(() => TranRunPingsAsync(db));
                var y = Task.Run(() => TranRunIntegersAsync(db));

                await x;
                await y;

                Writer.WriteLine(muxer.GetCounters().Interactive);
            }
        }

        private async Task TranRunIntegersAsync(IDatabase db)
        {
            var key = Me();
            await db.KeyDeleteAsync(key).ForAwait();
            await db.StringSetAsync(key, 1).ForAwait();
            Task[] tasks = new Task[InnerCount];
            for (int i = 0; i < IterationCount; i++)
            {
                var batch = db.CreateTransaction();
                batch.AddCondition(Condition.KeyExists(key));
                for (int j = 0; j < tasks.Length; j++)
                {
                    tasks[j] = batch.StringIncrementAsync(key);
                }
                await batch.ExecuteAsync().ForAwait();
                for (int j = tasks.Length - 1; j >= 0; j--)
                {
                    await tasks[j];
                }
            }

            var count = (long)await db.StringGetAsync(key).ForAwait();
            Writer.WriteLine($"tally: {count}");
        }

        private async Task TranRunPingsAsync(IDatabase db)
        {
            var key = Me();
            db.KeyDelete(key);
            Task[] tasks = new Task[InnerCount];
            for (int i = 0; i < IterationCount; i++)
            {
                var batch = db.CreateTransaction();
                batch.AddCondition(Condition.KeyNotExists(key));
                for (int j = 0; j < tasks.Length; j++)
                {
                    tasks[j] = batch.PingAsync();
                }
                await batch.ExecuteAsync().ForAwait();
                for (int j = tasks.Length - 1; j >= 0; j--)
                {
                    await tasks[j];
                }
            }
        }
    }
}
