using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace TestConsole
{
    internal static class Program
    {
        private static void Main()
        {
            RunCompetingBatchesOnSameMuxer();
        }
        static ConnectionMultiplexer Create()
        {
            var muxer = ConnectionMultiplexer.Connect("localhost:6379");
            muxer.GetDatabase().Ping();
            return muxer;
        }
        private const int IterationCount = 5000, InnerCount = 20;
        public static void RunCompetingBatchesOnSameMuxer()
        {
            using (var muxer = Create())
            {
                var db = muxer.GetDatabase();

                Thread x = new Thread(state => BatchRunPings((IDatabase)state));
                x.Name = nameof(BatchRunPings);
                Thread y = new Thread(state => BatchRunIntegers((IDatabase)state));
                y.Name = nameof(BatchRunIntegers);

                var watch = Stopwatch.StartNew();
                x.Start(db);
                y.Start(db);
                x.Join();
                y.Join();
                watch.Stop();
                Console.WriteLine($"{watch.ElapsedMilliseconds}ms");
                Console.WriteLine(muxer.GetCounters().Interactive);
                Console.WriteLine($"Service Counts: {SocketManager.Shared}");
            }
        }

        static RedisKey Me([CallerMemberName]string caller = null) => caller;

        private static void BatchRunIntegers(IDatabase db)
        {
            var key = Me();
            db.KeyDelete(key);
            db.StringSet(key, 1);
            Task[] tasks = new Task[InnerCount];
            for (int i = 0; i < IterationCount; i++)
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
            Console.WriteLine($"tally: {count}");
        }

        private static void BatchRunPings(IDatabase db)
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
    }
}
