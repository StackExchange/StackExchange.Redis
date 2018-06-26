using System;
using System.Collections.Generic;
using System.IO;
using StackExchange.Redis;

class Program
{
    static int Main()
    {
        var s = new StringWriter();
        try
        {
#if DEBUG
            Pipelines.Sockets.Unofficial.DebugCounters.SetLog(Console.Out);
#endif
            // it is sometimes hard to get the debugger to play nicely with benchmarkdotnet/xunit attached,
            // so this is just a *trivial* exe

            var config = new ConfigurationOptions
            {
                EndPoints = { "127.0.0.1" },
                //TieBreaker = "",
                //CommandMap = CommandMap.Create(new Dictionary<string, string>
                //{
                //    ["SUBSCRIBE"] = null,
                //    ["PSUBSCRIBE"] = null,
                //})
            };
            using (var conn = ConnectionMultiplexer.Connect(config, log: s))
            {
                Console.WriteLine("Connected");
                var db = conn.GetDatabase();
                db.StringSet("abc", "def");
                var x = db.StringGet("abc");
                Console.WriteLine(x);
                //for (int i = 0; i < 10; i++)
                //{
                //    Console.WriteLine($"Ping {i}");
                //    db.Ping();
                //}
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
            Console.WriteLine(s);
        }
    }
}
