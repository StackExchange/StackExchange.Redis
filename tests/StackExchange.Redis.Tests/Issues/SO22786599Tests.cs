using System.Diagnostics;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests.Issues;

public class SO22786599Tests : TestBase
{
    public SO22786599Tests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public void Execute()
    {
        string CurrentIdsSetDbKey = Me() + ".x";
        string CurrentDetailsSetDbKey = Me() + ".y";

        RedisValue[] stringIds = Enumerable.Range(1, 750).Select(i => (RedisValue)(i + " id")).ToArray();
        RedisValue[] stringDetails = Enumerable.Range(1, 750).Select(i => (RedisValue)(i + " detail")).ToArray();

        using var conn = Create();

        var db = conn.GetDatabase();
        var tran = db.CreateTransaction();

        tran.SetAddAsync(CurrentIdsSetDbKey, stringIds);
        tran.SetAddAsync(CurrentDetailsSetDbKey, stringDetails);

        var watch = Stopwatch.StartNew();
        var isOperationSuccessful = tran.Execute();
        watch.Stop();
        Log("{0}ms", watch.ElapsedMilliseconds);
        Assert.True(isOperationSuccessful);
    }
}
