using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using StackExchange.Redis;

class Program
{
    static int Main()
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
            using (var conn = ConnectionMultiplexer.Connect(config, log: s))
            {
                Execute(conn);
            }
            Console.WriteLine("Clean exit");
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
            Console.WriteLine($"{watch.ElapsedMilliseconds}ms");
            Console.WriteLine();

            //Console.WriteLine(s);
            Console.ReadKey();
            
        }
    }

    private static void Execute(ConnectionMultiplexer conn)
    {
        int pageSize = 100;
        RedisKey key = nameof(Execute);
        var db = conn.GetDatabase();
        db.KeyDelete(key);

        for (int i = 0; i < 2000; i++)
            db.SetAdd(key, "s" + i, flags: CommandFlags.FireAndForget);

        int count = db.SetScan(key, pageSize: pageSize).Count();

        Console.WriteLine(count == 2000 ? "Pass" : "Fail");
    }
}
