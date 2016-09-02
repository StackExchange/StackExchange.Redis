using StackExchange.Redis;
using System;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace BasicBench
{
    class Program
    {
        const int PipelinedCount = 5000000, RequestResponseCount = 100000,
            BatchSize = 1000, BatchCount = PipelinedCount / BatchSize;
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
                

                Console.WriteLine($"Sending {PipelinedCount} pings synchronously fire-and-forget (pipelined) ...");
                var timer = Stopwatch.StartNew();
                // starting at 1 so that we can wait on the last one and still send the right amount
                for (int i = 1; i < PipelinedCount; i++) db.Ping(CommandFlags.FireAndForget);
                db.Ping(); // block
                timer.Stop();
                Console.WriteLine($"{timer.ElapsedMilliseconds}ms; {((PipelinedCount * 1000.0) / timer.ElapsedMilliseconds):F0} ops/s");


                Console.WriteLine($"Sending {(BatchSize * BatchCount)+1} pings synchronously fire-and-forget ({BatchCount} batches of {BatchSize}) ...");
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
                timer = Stopwatch.StartNew();
                for (int i = 0; i < RequestResponseCount; i++) db.Ping();
                timer.Stop();
                Console.WriteLine($"{timer.ElapsedMilliseconds}ms; {((RequestResponseCount * 1000.0) / timer.ElapsedMilliseconds):F0} ops/s");

                PingAsync(db);


                Console.ReadKey();
            }

        }

        private static async void PingAsync(IDatabase db)
        {
            Console.WriteLine($"Sending {PipelinedCount} pings asynchronously fire-and-forget (pipelined) ...");
            var timer = Stopwatch.StartNew();
            // starting at 1 so that we can wait on the last one and still send the right amount
            for (int i = 1; i < PipelinedCount; i++) await db.PingAsync(CommandFlags.FireAndForget);
            await db.PingAsync(); // block
            timer.Stop();
            Console.WriteLine($"{timer.ElapsedMilliseconds}ms; {((PipelinedCount * 1000.0) / timer.ElapsedMilliseconds):F0} ops/s");


            Console.WriteLine($"Sending {(BatchSize * BatchCount) + 1} pings synchronously fire-and-forget ({BatchCount} batches of {BatchSize}) ...");
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
