using System;
using System.Linq;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace TestConsole
{
    internal static class Program
    {
        public static async Task Main()
        {
            var client = ConnectionMultiplexer.Connect("localhost");
            client.GetDatabase().Ping();
            var db = client.GetDatabase(0);

            var start = DateTime.Now;

            Show(client.GetCounters());
            var tasks = Enumerable.Range(0, 1000).Select(async i =>
            {
                int timeoutCount = 0;
                for (int t = 0; t < 1000; t++)
                {
                    try
                    {
                        await db.StringIncrementAsync(i.ToString(), 1);
                    }
                    catch (TimeoutException) { timeoutCount++; }
                }
                return timeoutCount;
            }).ToArray();

            await Task.WhenAll(tasks);
            int totalTimeouts = tasks.Sum(x => x.Result);
            Console.WriteLine("Total timeouts: " + totalTimeouts);
            Console.WriteLine();
            Show(client.GetCounters());

            var duration = DateTime.Now.Subtract(start).TotalMilliseconds;
            Console.WriteLine($"{duration}ms");
        }
        private static void Show(ServerCounters counters)
        {
            Console.WriteLine("CA: " + counters.Interactive.CompletedAsynchronously);
            Console.WriteLine("FA: " + counters.Interactive.FailedAsynchronously);
            Console.WriteLine("CS: " + counters.Interactive.CompletedSynchronously);
            Console.WriteLine();
        }
    }
}
