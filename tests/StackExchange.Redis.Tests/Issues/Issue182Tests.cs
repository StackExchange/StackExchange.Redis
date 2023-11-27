using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests.Issues;

public class Issue182Tests : TestBase
{
    protected override string GetConfiguration() => $"{TestConfig.Current.PrimaryServerAndPort},responseTimeout=10000";

    public Issue182Tests(ITestOutputHelper output) : base (output) { }

    [FactLongRunning]
    public async Task SetMembers()
    {
        using var conn = Create(syncTimeout: 20000);

        conn.ConnectionFailed += (s, a) =>
        {
            Log(a.FailureType.ToString());
            Log(a.Exception?.Message);
            Log(a.Exception?.StackTrace);
        };
        var db = conn.GetDatabase();

        var key = Me();
        const int count = (int)5e6;
        var len = await db.SetLengthAsync(key).ForAwait();

        if (len != count)
        {
            await db.KeyDeleteAsync(key).ForAwait();
            foreach (var _ in Enumerable.Range(0, count))
                db.SetAdd(key, Guid.NewGuid().ToByteArray(), CommandFlags.FireAndForget);

            Assert.Equal(count, await db.SetLengthAsync(key).ForAwait()); // SCARD for set
        }
        var result = await db.SetMembersAsync(key).ForAwait();
        Assert.Equal(count, result.Length); // SMEMBERS result length
    }

    [FactLongRunning]
    public async Task SetUnion()
    {
        using var conn = Create(syncTimeout: 10000);

        var db = conn.GetDatabase();

        var key1 = Me() + ":1";
        var key2 = Me() + ":2";
        var dstkey = Me() + ":dst";

        const int count = (int)5e6;

        var len1 = await db.SetLengthAsync(key1).ForAwait();
        var len2 = await db.SetLengthAsync(key2).ForAwait();
        await db.KeyDeleteAsync(dstkey).ForAwait();

        if (len1 != count || len2 != count)
        {
            await db.KeyDeleteAsync(key1).ForAwait();
            await db.KeyDeleteAsync(key2).ForAwait();

            foreach (var _ in Enumerable.Range(0, count))
            {
                db.SetAdd(key1, Guid.NewGuid().ToByteArray(), CommandFlags.FireAndForget);
                db.SetAdd(key2, Guid.NewGuid().ToByteArray(), CommandFlags.FireAndForget);
            }
            Assert.Equal(count, await db.SetLengthAsync(key1).ForAwait()); // SCARD for set 1
            Assert.Equal(count, await db.SetLengthAsync(key2).ForAwait()); // SCARD for set 2
        }
        await db.SetCombineAndStoreAsync(SetOperation.Union, dstkey, key1, key2).ForAwait();
        var dstLen = db.SetLength(dstkey);
        Assert.Equal(count * 2, dstLen); // SCARD for destination set
    }
}
