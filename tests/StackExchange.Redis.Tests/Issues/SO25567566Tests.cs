using System;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests.Issues;

public class SO25567566Tests : TestBase
{
    protected override string GetConfiguration() => TestConfig.Current.PrimaryServerAndPort;
    public SO25567566Tests(ITestOutputHelper output) : base(output) { }

    [FactLongRunning]
    public async Task Execute()
    {
        using var conn = ConnectionMultiplexer.Connect(GetConfiguration());

        for (int i = 0; i < 100; i++)
        {
            Assert.Equal("ok", await DoStuff(conn).ForAwait());
        }
    }

    private async Task<string> DoStuff(ConnectionMultiplexer conn)
    {
        var db = conn.GetDatabase();

        var timeout = Task.Delay(5000);
        var key = Me();
        var key2 = key + "2";
        var len = db.ListLengthAsync(key);

        if (await Task.WhenAny(timeout, len).ForAwait() != len)
        {
            return "Timeout getting length";
        }

        if ((await len.ForAwait()) == 0)
        {
            db.ListRightPush(key, "foo", flags: CommandFlags.FireAndForget);
        }
        var tran = db.CreateTransaction();
        var x = tran.ListRightPopLeftPushAsync(key, key2);
        var y = tran.SetAddAsync(key + "set", "bar");
        var z = tran.KeyExpireAsync(key2, TimeSpan.FromSeconds(60));
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
                db2.HashGet(key + "hash", "whatever");
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
