using System;
using System.Diagnostics;
using System.IO;
using StackExchange.Redis;

namespace TestConsole
{
    internal static class Program
    {
        private static int Main()
        {
            var s = new StringWriter();
            var watch = Stopwatch.StartNew();
            try
            {
#if DEBUG
                // Pipelines.Sockets.Unofficial.DebugCounters.SetLog(Console.Out);
#endif

                var config = new ConfigurationOptions
                {
                    ConnectRetry = 0,
                    EndPoints = { "127.0.0.1:6381" },
                    Password = "abc",
                };
                using (var conn = ConnectionMultiplexer.Connect(config, log: null))
                {
                    //Execute(conn);
                    

                }
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return -1;
            }
            finally
            {
                watch.Stop();
                Console.WriteLine();
                Console.WriteLine($"{watch.ElapsedMilliseconds}ms (done)");
            }
        }

        private static void Execute(ConnectionMultiplexer conn)
        {
            var key = "abc";
            Console.ReadKey();
            var db = conn.GetDatabase(0);
            var t = db.CreateTransaction();
            t.HashSetAsync(key, "foo", "bar");
            t.KeyExpireAsync(key, TimeSpan.FromSeconds(3600));
            t.Execute();
            Console.ReadKey();
        }
    }
}
