using System;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// A message that carries a key must report that key's hash slot, or a cluster client sends it to an arbitrary node
/// and leans on <c>-MOVED</c>: an extra round trip in the usual case, and a failure under
/// <see cref="CommandFlags.NoRedirect"/>. <c>INCREX</c> and <c>ARGREP</c> both shipped without one.
/// </summary>
public class KeyedMessageSlotTests(ITestOutputHelper output) : TestBase(output)
{
    private static readonly ServerSelectionStrategy Cluster = new(null!) { ServerType = ServerType.Cluster };

    private static void AssertRoutedByKey(Message message, RedisKey key)
    {
        var expected = Cluster.HashSlot(key);
        Assert.NotEqual(ServerSelectionStrategy.NoSlot, expected);
        Assert.Equal(expected, message.GetHashSlot(Cluster));
    }

    [Fact]
    public void Increx_Int64_IsRoutedByKey()
    {
        RedisKey key = "some-counter";
        var msg = new RedisDatabase.IncrexInt64Message(0, CommandFlags.None, key, 1, null, null, default, IncrementOptions.None);
        AssertRoutedByKey(msg, key);
        Assert.Equal("INCREX some-counter", msg.CommandAndKey);
    }

    [Fact]
    public void Increx_Double_IsRoutedByKey()
    {
        RedisKey key = "some-counter";
        var msg = new RedisDatabase.IncrexDoubleMessage(0, CommandFlags.None, key, 1.5, null, null, default, IncrementOptions.None);
        AssertRoutedByKey(msg, key);
    }

    [Fact]
    public void ArrayGrep_IsRoutedByKey()
    {
        RedisKey key = "some-array";
        AssertRoutedByKey(CreateGrep().CreateMessage(0, key, CommandFlags.None), key);
    }

    private static ArrayGrepRequest CreateGrep()
    {
        var request = new ArrayGrepRequest();
        request.AddPredicate(ArrayGrepRequest.Predicate.Match("alpha"));
        return request;
    }

    // enough keys that, sent to an arbitrary node, some would certainly miss their owner
    private const int KeyCount = 32;

    [Fact]
    public async Task Increx_OnCluster_NeedsNoRedirect()
    {
        await using var conn = Create(configuration: GetClusterConfiguration(), require: RedisFeatures.v8_8_0);
        var db = conn.GetDatabase();
        var expiry = Expiration.CreateOrKeepTtl(TimeSpan.FromMinutes(1), keepTtl: false);

        for (int i = 0; i < KeyCount; i++)
        {
            RedisKey key = Me() + ":" + i;
            await db.KeyDeleteAsync(key);

            // NoRedirect: lands on the slot owner first time, or throws
            var result = await db.StringIncrementAsync(key, 5, expiry, flags: CommandFlags.NoRedirect);
            Assert.Equal(5, result.Value);

            await db.KeyDeleteAsync(key, CommandFlags.FireAndForget);
        }
    }

    [Fact]
    public async Task ArrayGrep_OnCluster_NeedsNoRedirect()
    {
        await using var conn = Create(configuration: GetClusterConfiguration(), require: RedisFeatures.v8_8_0);
        var db = conn.GetDatabase();
        var grep = CreateGrep();

        for (int i = 0; i < KeyCount; i++)
        {
            RedisKey key = Me() + ":" + i;
            await db.KeyDeleteAsync(key);
            await db.ArraySetAsync(key, 0, "alpha");

            var found = await db.ArrayGrepAsync(key, grep, CommandFlags.NoRedirect);
            Assert.Equal(0, Assert.Single(found).Index);

            await db.KeyDeleteAsync(key, CommandFlags.FireAndForget);
        }
    }
}
