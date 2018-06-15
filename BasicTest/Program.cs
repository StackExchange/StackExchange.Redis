using StackExchange.Redis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System;

[assembly: AssemblyVersion("1.0.0")]

namespace BasicTest
{
    internal static class Program
    {
        public static void Main()
        {
            using (var conn = ConnectionMultiplexer.Connect("127.0.0.1:6379"))
            {
                var db = conn.GetDatabase();

                db.KeyDelete("abc");
                db.StringIncrement("abc");
                db.StringIncrement("abc", 15);
                db.StringIncrement("abc");
                int i = (int)db.StringGet("abc");
                Console.WriteLine(i);
            }
        }

        internal static string Me([CallerMemberName] string caller = null)
        {
            return caller;
        }
    }
}
