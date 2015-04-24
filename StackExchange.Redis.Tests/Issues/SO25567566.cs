using System;
using System.Threading.Tasks;
using NUnit.Framework;

namespace StackExchange.Redis.Tests.Issues
{
    [TestFixture]
    public class SO25567566 : TestBase
    {
        protected override string GetConfiguration()
        {
            return "127.0.0.1";
        }
        [Test]
        public async void Execute()
        {
            using(var conn = ConnectionMultiplexer.Connect(GetConfiguration())) // Create())
            {
                for(int i = 0; i < 100; i++)
                {
                    Assert.AreEqual("ok", await DoStuff(conn));

                }
            }
        }
        private async Task<string> DoStuff(ConnectionMultiplexer conn)
        {
            var db = conn.GetDatabase();

            var timeout = Task.Delay(5000);
            var len = db.ListLengthAsync("list");

            if (await Task.WhenAny(timeout, len) != len)
            {
                return "Timeout getting length";
            }

            
            if ((await len) == 0)
            {
                db.ListRightPush("list", "foo", flags: CommandFlags.FireAndForget);
            }
            var tran = db.CreateTransaction();
            var x = tran.ListRightPopLeftPushAsync("list", "list2");
            var y = tran.SetAddAsync("set", "bar");
            var z = tran.KeyExpireAsync("list2", TimeSpan.FromSeconds(60));
            timeout = Task.Delay(5000);

            var exec = tran.ExecuteAsync();
            // SWAP THESE TWO
            bool ok = await Task.WhenAny(exec, timeout) == exec;
            //bool ok = true;

            if (ok)
            {
                if (await exec)
                {
                    await Task.WhenAll(x, y, z);

                    var db2 = conn.GetDatabase();
                    db2.HashGet("hash", "whatever");
                    return "ok";
                }
                else
                {
                    return "Transaction aborted";
                }
            }
            else
            {
                return "Timeout during exec";
            }
        }
    }
}
