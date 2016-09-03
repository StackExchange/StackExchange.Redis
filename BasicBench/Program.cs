using StackExchange.Redis;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace BasicBench
{
    class Program
    {
        const int PipelinedCount = 5000, RequestResponseCount = 100,
            BatchSize = 1000, BatchCount = PipelinedCount / BatchSize,
            CorpusLoops = 10;
        static string[] GetCorpus()
        {
            Console.WriteLine(Directory.GetCurrentDirectory());
            return GetCorpus("TaleOfTwoCities.txt") ?? GetCorpus("../TaleOfTwoCities.txt") ?? new string[0];
        }

        static string[] GetCorpus(string path)
        {
            return File.Exists(path) ? File.ReadAllLines(path) : null;
        }
        static void Collect()
        {
            for (int i = 0; i < 5; i++)
            {
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
                GC.WaitForPendingFinalizers();
            }
        }
        public static void Main()
        {

            Thread.CurrentThread.Name = "Main";
            var config = new ConfigurationOptions
            {
                EndPoints = { new IPEndPoint(IPAddress.Loopback, 6379) },
                ResponseTimeout = 20000,
                SyncTimeout = 20000
            };
            using (var conn = ConnectionMultiplexer.Connect(config))
            {
                Thread.Sleep(1000);
                var db = conn.GetDatabase();
                if (db.IsConnected(default(RedisKey)))
                {
                    Console.WriteLine("StackExchange.Redis Connected successfully");
                }
                else
                {
                    Console.WriteLine("Failed to connect; is redis running?");
                    return;
                }
                Stopwatch timer;

                Console.WriteLine($"Sending {PipelinedCount} pings synchronously fire-and-forget (pipelined) ...");
                Collect();
                timer = Stopwatch.StartNew();
                // starting at 1 so that we can wait on the last one and still send the right amount
                for (int i = 1; i < PipelinedCount; i++) db.Ping(CommandFlags.FireAndForget);
                db.Ping(); // block
                timer.Stop();
                Console.WriteLine($"{timer.ElapsedMilliseconds}ms; {((PipelinedCount * 1000.0) / timer.ElapsedMilliseconds):F0} ops/s");


                Console.WriteLine($"Sending {(BatchSize * BatchCount) + 1} pings synchronously fire-and-forget ({BatchCount} batches of {BatchSize}) ...");
                Collect();
                timer = Stopwatch.StartNew();
                for (int i = 0; i < BatchCount; i++)
                {
                    var batch = db.CreateBatch();
                    for (int j = 0; j < BatchSize; j++)
                    {
                        batch.PingAsync(CommandFlags.FireAndForget);
                    }
                    batch.Execute();
                }
                db.Ping(); // block
                timer.Stop();
                Console.WriteLine($"{timer.ElapsedMilliseconds}ms; {((((BatchSize * BatchCount) + 1) * 1000.0) / timer.ElapsedMilliseconds):F0} ops/s");

                Console.WriteLine($"Sending {RequestResponseCount} pings synchronously req/resp/req/resp/...");
                Collect();
                timer = Stopwatch.StartNew();
                for (int i = 0; i < RequestResponseCount; i++) db.Ping();
                timer.Stop();
                Console.WriteLine($"{timer.ElapsedMilliseconds}ms; {((RequestResponseCount * 1000.0) / timer.ElapsedMilliseconds):F0} ops/s");

                //Console.WriteLine("Loading corpus...");
                //var corpus = GetCorpus();
                //var received = new string[corpus.Length];
                //Console.WriteLine($"Sending {CorpusLoops * corpus.Length} echoes synchronously req/resp/req/resp/...");
                //var server = conn.GetServer(conn.GetEndPoints().Single());
                //Collect();
                //timer = Stopwatch.StartNew();
                //for (int j = 0; j < CorpusLoops; j++)
                //{
                //    for (int i = 0; i < corpus.Length; i++)
                //    {
                //        received[i] = server.Echo(corpus[i]);
                //    }
                //}
                //timer.Stop();
                //Console.WriteLine($"{timer.ElapsedMilliseconds}ms; {((CorpusLoops * corpus.Length * 1000.0) / timer.ElapsedMilliseconds):F0} ops/s");
                //Console.WriteLine($"Correct data received: {received.SequenceEqual(corpus)}");


                PingAsync(db);


                Console.ReadKey();
            }

        }

        private static async void PingAsync(IDatabase db)
        {
            Console.WriteLine($"Sending {PipelinedCount} pings asynchronously fire-and-forget (pipelined) ...");
            Collect();
            var timer = Stopwatch.StartNew();
            // starting at 1 so that we can wait on the last one and still send the right amount
            for (int i = 1; i < PipelinedCount; i++) await db.PingAsync(CommandFlags.FireAndForget);
            await db.PingAsync(); // block
            timer.Stop();
            Console.WriteLine($"{timer.ElapsedMilliseconds}ms; {((PipelinedCount * 1000.0) / timer.ElapsedMilliseconds):F0} ops/s");


            Console.WriteLine($"Sending {(BatchSize * BatchCount) + 1} pings synchronously fire-and-forget ({BatchCount} batches of {BatchSize}) ...");
            Collect();
            timer = Stopwatch.StartNew();
            Task ignored = null;
            for (int i = 0; i < BatchCount; i++)
            {
                var batch = db.CreateBatch();
                for (int j = 0; j < BatchSize; j++)
                {
                    ignored = batch.PingAsync(CommandFlags.FireAndForget);
                }
                batch.Execute();
            }
            if (ignored != null) await ignored;
            await db.PingAsync(); // block
            timer.Stop();
            Console.WriteLine($"{timer.ElapsedMilliseconds}ms; {((((BatchSize * BatchCount) + 1) * 1000.0) / timer.ElapsedMilliseconds):F0} ops/s");

            Console.WriteLine($"Sending {RequestResponseCount} pings asynchronously req/resp/req/resp/...");
            timer = Stopwatch.StartNew();
            for (int i = 0; i < RequestResponseCount; i++) await db.PingAsync();
            timer.Stop();
            Console.WriteLine($"{timer.ElapsedMilliseconds}ms; {((RequestResponseCount * 1000.0) / timer.ElapsedMilliseconds):F0} ops/s");
        }
    }
}
