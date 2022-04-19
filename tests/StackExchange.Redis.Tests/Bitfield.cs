using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests;

[Collection(SharedConnectionFixture.Key)]
public class Bitfield : TestBase
{
    public Bitfield(ITestOutputHelper output, SharedConnectionFixture fixture) : base(output, fixture) { }

    [Fact]
    public void TestBitfieldHappyPath()
    {

        using var conn = Create(require: RedisFeatures.v3_2_0);
        var db = conn.GetDatabase();
        RedisKey key = Me();
        db.KeyDelete(key);

        var encoding = new BitfieldEncoding(Signedness.Signed, 10);
        var offset = new BitfieldOffset(true, 1);

        // should be the old value
        var setResult = db.StringBitfieldSet(key, encoding, offset, -255);
        var getResult = db.StringBitfieldGet(key, encoding, offset);
        var incrementResult = db.StringBitfieldIncrement(key, encoding, offset, -10);
        Assert.Equal(0, setResult);
        Assert.Equal(-255, getResult);
        Assert.Equal(-265, incrementResult);

        encoding = new BitfieldEncoding(Signedness.Unsigned, 18);
        offset = new BitfieldOffset(false, 22);

        setResult = db.StringBitfieldSet(key, encoding, offset, 262123);
        getResult = db.StringBitfieldGet(key, encoding, offset);
        incrementResult = db.StringBitfieldIncrement(key, encoding, offset, 20);

        Assert.Equal(0, setResult);
        Assert.Equal(262123, getResult);
        Assert.Equal(262143, incrementResult);
    }

    [Fact]
    public async Task TestBitfieldHappyPathAsync()
    {

        using var conn = Create(require: RedisFeatures.v3_2_0);
        var db = conn.GetDatabase();
        RedisKey key = Me();
        db.KeyDelete(key);

        var encoding = new BitfieldEncoding(Signedness.Signed, 10);
        var offset = new BitfieldOffset(true, 1);

        // should be the old value
        var setResult = await db.StringBitfieldSetAsync(key, encoding, offset, -255);
        var getResult = await db.StringBitfieldGetAsync(key, encoding, offset);
        var incrementResult = await db.StringBitfieldIncrementAsync(key, encoding, offset, -10);
        Assert.Equal(0, setResult);
        Assert.Equal(-255, getResult);
        Assert.Equal(-265, incrementResult);

        encoding = new BitfieldEncoding(Signedness.Unsigned, 18);
        offset = new BitfieldOffset(false, 22);

        setResult = await db.StringBitfieldSetAsync(key, encoding, offset, 262123);
        getResult = await db.StringBitfieldGetAsync(key, encoding, offset);
        incrementResult = await db.StringBitfieldIncrementAsync(key, encoding, offset, 20);

        Assert.Equal(0, setResult);
        Assert.Equal(262123, getResult);
        Assert.Equal(262143, incrementResult);
    }

    [Fact]
    public async Task TestBitfieldMulti()
    {
        using var conn = Create(require: RedisFeatures.v3_2_0);
        var db = conn.GetDatabase();
        RedisKey key = Me();
        db.KeyDelete(key);

        var set1 = new BitfieldSet(new BitfieldEncoding(Signedness.Unsigned, 3), new BitfieldOffset(false, 5), 7);
        var get1 = new BitfieldGet(new BitfieldEncoding(Signedness.Unsigned, 3), new BitfieldOffset(false, 5));
        var incr1 = new BitfieldIncrby(new BitfieldEncoding(Signedness.Unsigned, 3), new BitfieldOffset(false, 5), -1);

        var set2 = new BitfieldSet(new BitfieldEncoding(Signedness.Signed, 45), new BitfieldOffset(true, 1), 17592186044415);
        var get2 = new BitfieldGet(new BitfieldEncoding(Signedness.Signed, 45), new BitfieldOffset(true, 1));
        var incr2 = new BitfieldIncrby(new BitfieldEncoding(Signedness.Signed, 45), new BitfieldOffset(true, 1), 1, BitfieldOverflowHandling.Fail);

        var subcommands = new BitfieldSubCommand[] {set1, get1, incr1, set2, get2, incr2};
        var res = await db.StringBitfieldAsync(key, subcommands);

        Assert.Equal(0, res[0]);
        Assert.Equal(7, res[1]);
        Assert.Equal(6, res[2]);
        Assert.Equal(0, res[3]);
        Assert.Equal(17592186044415, res[4]);
        Assert.Null(res[5]);
    }

    [Fact]
    public async Task TestOverflows()
    {
        using var conn = Create(require: RedisFeatures.v3_2_0);
        var db = conn.GetDatabase();
        RedisKey key = Me();
        db.KeyDelete(key);

        var encoding = new BitfieldEncoding(Signedness.Signed, 3);
        var offset = new BitfieldOffset(true, 3);

        await db.StringBitfieldSetAsync(key, encoding, offset, 3);
        var incrFail = await db.StringBitfieldIncrementAsync(key, encoding, offset, 1, BitfieldOverflowHandling.Fail);
        Assert.Null(incrFail);
        var incrWrap = await db.StringBitfieldIncrementAsync(key, encoding, offset, 1);
        Assert.Equal(-4, incrWrap);
        await db.StringBitfieldSetAsync(key, encoding, offset, 3);
        var incrSat = await db.StringBitfieldIncrementAsync(key, encoding, offset, 1, BitfieldOverflowHandling.Saturate);
        Assert.Equal(3, incrSat);
    }
}
