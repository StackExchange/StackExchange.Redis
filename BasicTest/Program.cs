using System;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace BasicTest
{
    internal static class Program
    {
        public static async Task Main()
        {
            using (var conn = await ConnectionMultiplexer.ConnectAsync("127.0.0.1:6379"))
            {
                var db = conn.GetDatabase(3);

                var batch = db.CreateBatch();
                var del = batch.KeyDeleteAsync("abc");
                var set = batch.StringSetAsync("abc", "Does SE.Redis work on System.IO.Pipelines?");
                var s = batch.StringGetAsync("abc");
                batch.Execute();

                await del;
                await set;
                Console.WriteLine(await s);

                var rand = new Random(12345);
                RedisKey counter = "counter";
                db.KeyDelete(counter, CommandFlags.FireAndForget);
                int expected = 0;
                for (int i = 0; i < 1000; i++)
                {
                    int x = rand.Next(50);
                    expected += x;
                    db.StringIncrement(counter, x, CommandFlags.FireAndForget);
                }
                int actual = (int)db.StringGet(counter);
                Console.WriteLine($"{expected} vs {actual}");
            }
        }
    }
}
