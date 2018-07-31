using System;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests.Issues
{
    public class SO25567566 : TestBase
    {
        protected override string GetConfiguration() => TestConfig.Current.MasterServerAndPort;
        public SO25567566(ITestOutputHelper output) : base(output) { }

        [FactLongRunning]
        public async Task Execute()
        {
            using (var conn = ConnectionMultiplexer.Connect(GetConfiguration())) // Create())
            {
                for (int i = 0; i < 100; i++)
                {
                    Assert.Equal("ok", await DoStuff(conn).ForAwait());
                }
            }
        }

        private async Task<string> DoStuff(ConnectionMultiplexer conn)
        {
            var db = conn.GetDatabase();

            var timeout = Task.Delay(5000);
            var len = db.ListLengthAsync("list");

            if (await Task.WhenAny(timeout, len).ForAwait() != len)
            {
                return "Timeout getting length";
            }

            if ((await len.ForAwait()) == 0)
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
            bool ok = await Task.WhenAny(exec, timeout).ForAwait() == exec;
            //bool ok = true;

            if (ok)
            {
                if (await exec.ForAwait())
                {
                    await Task.WhenAll(x, y, z).ForAwait();

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
