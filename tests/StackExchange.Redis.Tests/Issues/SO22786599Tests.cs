using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests.Issues;

public class SO22786599Tests(ITestOutputHelper output) : TestBase(output)
{
    [Fact]
    public async Task Execute()
    {
        string currentIdsSetDbKey = Me() + ".x";
        string currentDetailsSetDbKey = Me() + ".y";

        RedisValue[] stringIds = Enumerable.Range(1, 750).Select(i => (RedisValue)(i + " id")).ToArray();
        RedisValue[] stringDetails = Enumerable.Range(1, 750).Select(i => (RedisValue)(i + " detail")).ToArray();

        await using var conn = Create();

        var db = conn.GetDatabase();
        var tran = db.CreateTransaction();

        _ = tran.SetAddAsync(currentIdsSetDbKey, stringIds);
        _ = tran.SetAddAsync(currentDetailsSetDbKey, stringDetails);

        var watch = Stopwatch.StartNew();
        var isOperationSuccessful = tran.Execute();
        watch.Stop();
        Log("{0}ms", watch.ElapsedMilliseconds);
        Assert.True(isOperationSuccessful);
    }
}
