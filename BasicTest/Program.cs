using StackExchange.Redis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System;

[assembly: AssemblyVersion("1.0.0")]

namespace BasicTest
{
    static class Program
    {
        static void Main()
        {
            using (var conn = ConnectionMultiplexer.Connect("127.0.0.1:6379"))
            {
                var db = conn.GetDatabase();

                RedisKey key = Me();
                db.KeyDelete(key);
                db.StringSet(key, "abc");

                string s = (string)db.ScriptEvaluate(@"
    local val = redis.call('get', KEYS[1])
    redis.call('del', KEYS[1])
    return val", new RedisKey[] { key }, flags: CommandFlags.NoScriptCache);

                Console.WriteLine(s);
                Console.WriteLine(db.KeyExists(key));

            }
        }

        internal static string Me([CallerMemberName] string caller = null)
        {
            return caller;
        }
    }
}
