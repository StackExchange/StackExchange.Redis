using System;
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

        var offset = 1;
        byte width = 10;
        var byBit = false;
        var unsigned = false;


        // should be the old value
        var setResult = db.StringBitfieldSet(key, offset, width, -255, byBit, unsigned);
        var getResult = db.StringBitfieldGet(key, offset, width, byBit, unsigned);
        var incrementResult = db.StringBitfieldIncrement(key, offset, width, -10, byBit, unsigned);
        Assert.Equal(0, setResult);
        Assert.Equal(-255, getResult);
        Assert.Equal(-265, incrementResult);

        width = 18;
        unsigned = true;
        offset = 22;
        byBit = true;

        setResult = db.StringBitfieldSet(key, offset, width, 262123, byBit, unsigned);
        getResult = db.StringBitfieldGet(key, offset, width, byBit, unsigned);
        incrementResult = db.StringBitfieldIncrement(key, offset, width, 20, byBit, unsigned);

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

        var offset = 1;
        byte width = 10;
        var byBit = false;
        var unsigned = false;

        // should be the old value
        var setResult = await db.StringBitfieldSetAsync(key, offset, width, -255, byBit, unsigned);
        var getResult = await db.StringBitfieldGetAsync(key, offset, width, byBit, unsigned);
        var incrementResult = await db.StringBitfieldIncrementAsync(key, offset, width, -10, byBit, unsigned);
        Assert.Equal(0, setResult);
        Assert.Equal(-255, getResult);
        Assert.Equal(-265, incrementResult);

        width = 18;
        unsigned = true;
        offset = 22;
        byBit = true;

        setResult = await db.StringBitfieldSetAsync(key, offset, width, 262123, byBit, unsigned);
        getResult = await db.StringBitfieldGetAsync(key, offset, width, byBit, unsigned);
        incrementResult = await db.StringBitfieldIncrementAsync(key, offset, width, 20, byBit, unsigned);

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

        var subCommands = new[]
        {
            BitfieldOperation.Set(5, 3, 7, true, true),
            BitfieldOperation.Get(5, 3, true, true),
            BitfieldOperation.Increment(5, 3, -1, true, true),
            BitfieldOperation.Set(1, 45, 17592186044415, false, false),
            BitfieldOperation.Get(1, 45, false, false),
            BitfieldOperation.Increment(1, 45, 1, false, false, BitfieldOverflowHandling.Fail)
        };

        var res = await db.StringBitfieldAsync(key, subCommands);

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

        var offset = 3;
        byte width = 3;
        var byBit = false;
        var unsigned = false;

        await db.StringBitfieldSetAsync(key, offset, width, 3, byBit, unsigned);
        var incrFail = await db.StringBitfieldIncrementAsync(key, offset, width, 1, byBit, unsigned, BitfieldOverflowHandling.Fail);
        Assert.Null(incrFail);
        var incrWrap = await db.StringBitfieldIncrementAsync(key, offset, width, 1, byBit, unsigned);
        Assert.Equal(-4, incrWrap);
        await db.StringBitfieldSetAsync(key, offset, width, 3, byBit, unsigned);
        var incrSat = await db.StringBitfieldIncrementAsync(key, offset, width, 1, byBit, unsigned, BitfieldOverflowHandling.Saturate);
        Assert.Equal(3, incrSat);
    }

    [Fact]
    public void PreflightValidation()
    {
        using var conn = Create(require: RedisFeatures.v3_2_0);
        var db = conn.GetDatabase();
        RedisKey key = Me();
        db.KeyDelete(key);

        Assert.Throws<ArgumentException>(()=> db.StringBitfieldGet(key, 0, 0, false, false));
        Assert.Throws<ArgumentException>(()=> db.StringBitfieldGet(key, 64, 0, false, true));
        Assert.Throws<ArgumentException>(()=> db.StringBitfieldGet(key, 65, 0, false, false));
        Assert.Throws<ArgumentNullException>(() => db.StringBitfield(key, new[] { new BitfieldOperation() { Offset = "0", SubCommand = BitFieldSubCommand.Set, Encoding = "i5" } }));
    }
}
