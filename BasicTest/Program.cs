using System;
using System.Diagnostics;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace BasicTest
{
    internal static class Program
    {
        public static async Task Main()
        {
            using (var conn = await ConnectionMultiplexer.ConnectAsync("127.0.0.1:6379,syncTimeout=2000"))
            {
                int expected = 0;

                try
                {

                    conn.ConnectionFailed += (sender, e) => Console.WriteLine($"{e.ConnectionType}, {e.FailureType}: {e.Exception.Message}");
                    var db = conn.GetDatabase(3);

                    var batch = db.CreateBatch();
                    var del = batch.KeyDeleteAsync("abc");
                    var set = batch.StringSetAsync("abc", "Does SE.Redis work on System.IO.Pipelines?");
                    var s = batch.StringGetAsync("abc");
                    batch.Execute();

                    await del;
                    await set;
                    Console.WriteLine(await s);

                    const int COUNT = 10000;
                    var rand = new Random(12345);
                    RedisKey counter = "counter";
                    var watch = Stopwatch.StartNew();
                    db.KeyDelete(counter, CommandFlags.FireAndForget);
                    for (int i = 0; i < COUNT; i++)
                    {
                        int x = rand.Next(50);
                        expected += x;
                        db.StringIncrement(counter, x, CommandFlags.FireAndForget);
                    }
                    int actual = (int)await db.StringGetAsync(counter);
                    watch.Stop();
                    Console.WriteLine($"{expected} vs {actual}, {watch.ElapsedMilliseconds}ms for {COUNT} incrby");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"expected when fail: {expected}");
                    Console.WriteLine(ex.Message);
                }
            }
        }
    }
}
