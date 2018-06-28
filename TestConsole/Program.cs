using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using StackExchange.Redis;

class Program
{
    static void Main()
    {
        var arr = typeof(ConnectionMultiplexer).Assembly.GetTypes();
        Array.Sort(arr, (x, y) => string.CompareOrdinal(x.FullName, y.FullName));
        foreach (var type in arr)
        {
            if (type.IsPublic)
            {
                Console.WriteLine($"[assembly:TypeForwardedTo(typeof(global::{type.FullName}))]");
            }
        }
    }
    static int Main2()
    {
        var s = new StringWriter();
        try
        {
#if DEBUG
            // Pipelines.Sockets.Unofficial.DebugCounters.SetLog(Console.Out);
#endif
            
            var config = new ConfigurationOptions
            {
                EndPoints = { "127.0.0.1" },
                
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
            Console.WriteLine();
            // Console.WriteLine(s);
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
