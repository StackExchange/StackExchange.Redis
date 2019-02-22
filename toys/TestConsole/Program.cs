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
                for (int t = 0; t < 1000; t++)
                {
                    await db.StringIncrementAsync(i.ToString(), 1);
                    // db.StringIncrement(i.ToString(), 1);
                }
                await Task.Yield();
            });

            await Task.WhenAll(tasks);
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
