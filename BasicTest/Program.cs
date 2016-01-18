using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis;

[assembly: AssemblyVersion("1.0.0")]

namespace BasicTest
{
    class Program
    {
        static void Main(string[] args)
        {
            int AsyncOpsQty = 10000;
            if(args.Length == 1)
            {
                int tmp;
                if(int.TryParse(args[0], out tmp))
                    AsyncOpsQty = tmp;
            }
            MassiveBulkOpsAsync(AsyncOpsQty, true, true);
            MassiveBulkOpsAsync(AsyncOpsQty, true, false);
            MassiveBulkOpsAsync(AsyncOpsQty, false, true);
            MassiveBulkOpsAsync(AsyncOpsQty, false, false);
        }
        static void MassiveBulkOpsAsync(int AsyncOpsQty, bool preserveOrder, bool withContinuation)
        {            
            using (var muxer = ConnectionMultiplexer.Connect("localhost,resolvedns=1"))
            {
                muxer.PreserveAsyncOrder = preserveOrder;
                RedisKey key = "MBOA";
                var conn = muxer.GetDatabase();
                muxer.Wait(conn.PingAsync());

#if DNXCORE50
                int number = 0;
#endif
                Action<Task> nonTrivial = delegate
                {
#if !DNXCORE50
                    Thread.SpinWait(5);
#else
                    for (int i = 0; i < 50; i++)
                    {
                        number++;
                    }
#endif
                };
                var watch = Stopwatch.StartNew();
                for (int i = 0; i <= AsyncOpsQty; i++)
                {
                    var t = conn.StringSetAsync(key, i);
                    if (withContinuation) t.ContinueWith(nonTrivial);
                }
                int val = (int)muxer.Wait(conn.StringGetAsync(key));
                watch.Stop();

                Console.WriteLine("After {0}: {1}", AsyncOpsQty, val);
                Console.WriteLine("({3}, {4})\r\n{2}: Time for {0} ops: {1}ms; ops/s: {5}", AsyncOpsQty, watch.ElapsedMilliseconds, Me(),
                    withContinuation ? "with continuation" : "no continuation", preserveOrder ? "preserve order" : "any order",
                    AsyncOpsQty / watch.Elapsed.TotalSeconds);
            }
        }
        protected static string Me([CallerMemberName] string caller = null)
        {
            return caller;
        }

    }
}
