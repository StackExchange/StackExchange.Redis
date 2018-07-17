using System;
using System.Diagnostics;

namespace TestConsole
{
    internal static class Program
    {
        private static int Main()
        {
            try
            {
                using (var obj = new BasicTest.RedisBenchmarks())
                {
                    var watch = Stopwatch.StartNew();
                    obj.ExecuteIncrBy();
                    watch.Stop();
                    Console.WriteLine($"{watch.ElapsedMilliseconds}ms");
                }
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return -1;
            }
        }
    }
}
