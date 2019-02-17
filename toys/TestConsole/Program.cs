using System;
using StackExchange.Redis;

namespace TestConsole
{
    internal static class Program
    {
        public static void Main()
        {
            var mux = new WaitAwaitMutex(0);
            using (var token = mux.TryWait())
            {
                Console.WriteLine(token.Success);
                using (var t2 = mux.TryWait())
                {
                    Console.WriteLine(t2.Success);
                }
            }

            using (var token = mux.TryWait())
            {
                Console.WriteLine(token.Success);
            }
        }
    }
}
