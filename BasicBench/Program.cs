using StackExchange.Redis;
using System;
using System.Diagnostics;
using System.Threading;

namespace BasicBench
{
    class Program
    {
        const int PipelinedCount = 5000000, RequestResponseCount = 100000;
        public static void Main()
        {

            Thread.CurrentThread.Name = "Main";
            using (var conn = ConnectionMultiplexer.Connect("127.0.0.1:6379"))
            {
                Thread.Sleep(1000);
                var server = conn.GetServer("127.0.0.1:6379");
                if (server.IsConnected)
                {
                    Console.WriteLine("Connected successfully");
                }
                else
                {
                    Console.WriteLine("Failed to connect; is redis running?");
                    return;
                }

                
                Console.WriteLine($"Sending {PipelinedCount} pings synchronously fire-and-forget (pipelined) ...");
                var timer = Stopwatch.StartNew();
                // starting at 1 so that we can wait on the last one and still send the right amount
                for (int i = 1; i < PipelinedCount; i++) server.Ping(CommandFlags.FireAndForget);
                server.Ping(); // block
                timer.Stop();
                Console.WriteLine($"{timer.ElapsedMilliseconds}ms; {((PipelinedCount * 1000.0) / timer.ElapsedMilliseconds):F0} ops/s");

                Console.WriteLine($"Sending {RequestResponseCount} pings synchronously req/resp/req/resp/...");
                timer = Stopwatch.StartNew();
                for (int i = 0; i < RequestResponseCount; i++) server.Ping();
                timer.Stop();
                Console.WriteLine($"{timer.ElapsedMilliseconds}ms; {((RequestResponseCount * 1000.0) / timer.ElapsedMilliseconds):F0} ops/s");

                PingAsync(server);


                Console.ReadKey();
            }

        }

        private static async void PingAsync(IServer server)
        {
            Console.WriteLine($"Sending {PipelinedCount} pings asynchronously fire-and-forget (pipelined) ...");
            var timer = Stopwatch.StartNew();
            // starting at 1 so that we can wait on the last one and still send the right amount
            for (int i = 1; i < PipelinedCount; i++) await server.PingAsync(CommandFlags.FireAndForget);
            await server.PingAsync(); // block
            timer.Stop();
            Console.WriteLine($"{timer.ElapsedMilliseconds}ms; {((PipelinedCount * 1000.0) / timer.ElapsedMilliseconds):F0} ops/s");

            Console.WriteLine($"Sending {RequestResponseCount} pings asynchronously req/resp/req/resp/...");
            timer = Stopwatch.StartNew();
            for (int i = 0; i < RequestResponseCount; i++) await server.PingAsync();
            timer.Stop();
            Console.WriteLine($"{timer.ElapsedMilliseconds}ms; {((RequestResponseCount * 1000.0) / timer.ElapsedMilliseconds):F0} ops/s");
        }
    }
}
