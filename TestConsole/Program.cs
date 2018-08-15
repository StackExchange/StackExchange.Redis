using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace TestConsole
{
    internal static class Program
    {
        private static void Main()
        {
            Console.WriteLine($"{Environment.OSVersion} / {Environment.Version} / {(Environment.Is64BitProcess ? "64" : "32")}");
            Console.WriteLine(RuntimeInformation.FrameworkDescription);
            DateTime stop = DateTime.UtcNow.AddSeconds(30);
            int i = 0;
            do
            {
                Console.WriteLine(i++);
                RunCompetingBatchesOnSameMuxer();
            } while (DateTime.UtcNow < stop);
        }
        private static ConnectionMultiplexer Create()
        {
            var options = new ConfigurationOptions
            {
                EndPoints = { "localhost:6379" },
                SyncTimeout = int.MaxValue,
                // CommandMap = CommandMap.Create(new HashSet<string> { "subscribe", "psubscsribe", "publish" }, false),
            };
            var muxer = ConnectionMultiplexer.Connect(options);
            muxer.GetDatabase().Ping();
            return muxer;
        }
        private const int IterationCount = 500, InnerCount = 20;
        public static void RunCompetingBatchesOnSameMuxer()
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

        private static RedisKey Me([CallerMemberName]string caller = null) => caller;

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
                if (i % 1000 == 0) Console.WriteLine(i);
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
                    tasks[j] = batch.ExecuteAsync("echo", "echo" + j);
                }
                batch.Execute();
                db.Multiplexer.WaitAll(tasks);
                if (i % 1000 == 0) Console.WriteLine(i);
            }
        }
    }
}
