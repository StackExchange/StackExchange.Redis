using System;
using System.Threading.Tasks;
using StackExchange.Redis.KeyspaceIsolation;
using Xunit;

namespace StackExchange.Redis.Tests;

[Collection(SharedConnectionFixture.Key)]
public class WithKeyPrefixTests(ITestOutputHelper output, SharedConnectionFixture fixture) : TestBase(output, fixture)
{
    [Fact]
    public async Task BlankPrefixYieldsSame_Bytes()
    {
        await using var conn = Create();

        var raw = conn.GetDatabase();
        var prefixed = raw.WithKeyPrefix(Array.Empty<byte>());
        Assert.Same(raw, prefixed);
    }

    [Fact]
    public async Task BlankPrefixYieldsSame_String()
    {
        await using var conn = Create();

        var raw = conn.GetDatabase();
        var prefixed = raw.WithKeyPrefix("");
        Assert.Same(raw, prefixed);
    }

    [Fact]
    public async Task NullPrefixIsError_Bytes()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await using var conn = Create();

            var raw = conn.GetDatabase();
            raw.WithKeyPrefix((byte[]?)null);
        });
    }

    [Fact]
    public async Task NullPrefixIsError_String()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await using var conn = Create();

            var raw = conn.GetDatabase();
            raw.WithKeyPrefix((string?)null);
        });
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData(null)]
    public void NullDatabaseIsError(string? prefix)
    {
        Assert.Throws<ArgumentNullException>(() =>
        {
            IDatabase? raw = null;
            raw!.WithKeyPrefix(prefix);
        });
    }

    [Fact]
    public async Task BasicSmokeTest()
    {
        await using var conn = Create();

        var raw = conn.GetDatabase();

        var prefix = Me();
        var foo = raw.WithKeyPrefix(prefix);
        var foobar = foo.WithKeyPrefix("bar");

        string key = Me();

        string s = Guid.NewGuid().ToString(), t = Guid.NewGuid().ToString();

        foo.StringSet(key, s, flags: CommandFlags.FireAndForget);
        var val = (string?)foo.StringGet(key);
        Assert.Equal(s, val); // fooBasicSmokeTest

        foobar.StringSet(key, t, flags: CommandFlags.FireAndForget);
        val = foobar.StringGet(key);
        Assert.Equal(t, val); // foobarBasicSmokeTest

        val = foo.StringGet("bar" + key);
        Assert.Equal(t, val); // foobarBasicSmokeTest

        val = raw.StringGet(prefix + key);
        Assert.Equal(s, val); // fooBasicSmokeTest

        val = raw.StringGet(prefix + "bar" + key);
        Assert.Equal(t, val); // foobarBasicSmokeTest
    }

    [Fact]
    public async Task ConditionTest()
    {
        await using var conn = Create();

        var raw = conn.GetDatabase();

        var prefix = Me() + ":";
        var foo = raw.WithKeyPrefix(prefix);

        raw.KeyDelete(prefix + "abc", CommandFlags.FireAndForget);
        raw.KeyDelete(prefix + "i", CommandFlags.FireAndForget);

        // execute while key exists
        raw.StringSet(prefix + "abc", "def", flags: CommandFlags.FireAndForget);
        var tran = foo.CreateTransaction();
        tran.AddCondition(Condition.KeyExists("abc"));
        _ = tran.StringIncrementAsync("i");
        tran.Execute();

        int i = (int)raw.StringGet(prefix + "i");
        Assert.Equal(1, i);

        // repeat without key
        raw.KeyDelete(prefix + "abc", CommandFlags.FireAndForget);
        tran = foo.CreateTransaction();
        tran.AddCondition(Condition.KeyExists("abc"));
        _ = tran.StringIncrementAsync("i");
        tran.Execute();

        i = (int)raw.StringGet(prefix + "i");
        Assert.Equal(1, i);
    }
}
