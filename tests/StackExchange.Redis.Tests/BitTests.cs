using System;
using System.Linq;
using System.Threading.Tasks;
using StackExchange.Redis.Profiling;
using Xunit;

namespace StackExchange.Redis.Tests;

[RunPerProtocol]
public class BitTests(ITestOutputHelper output, SharedConnectionFixture fixture) : TestBase(output, fixture)
{
    [Fact]
    public async Task BasicOps()
    {
        await using var conn = Create();
        var db = conn.GetDatabase();
        RedisKey key = Me();

        db.KeyDelete(key, CommandFlags.FireAndForget);
        db.StringSetBit(key, 10, true);
        Assert.True(db.StringGetBit(key, 10));
        Assert.False(db.StringGetBit(key, 11));
    }

    [Fact]
    public async Task BitPositionUnboundedEndLooksPastEndOfString()
    {
        await using var conn = Create();
        var db = conn.GetDatabase();
        RedisKey key = Me();

        db.KeyDelete(key, CommandFlags.FireAndForget);
        for (int i = 0; i < 8; i++)
        {
            db.StringSetBit(key, i, true, CommandFlags.FireAndForget);
        }
        Assert.Equal(1, db.StringLength(key));

        // an explicit end confines the search to the string, which has no clear bit
        Assert.Equal(-1, db.StringBitPosition(key, false));
        Assert.Equal(-1, await db.StringBitPositionAsync(key, false, 0, -1));

        // an open-ended range reports the first clear bit past the end of the string
        Assert.Equal(8, db.StringBitPosition(key, false, 0, StringIndex.Unbounded));
        Assert.Equal(8, await db.StringBitPositionAsync(key, false, 0, StringIndex.Unbounded));

        // set bits are unaffected either way
        Assert.Equal(0, db.StringBitPosition(key, true));
        Assert.Equal(0, db.StringBitPosition(key, true, 0, StringIndex.Unbounded));
    }

    [Fact]
    public async Task BitFieldBasicOps()
    {
        await using var conn = Create(require: RedisFeatures.v3_2_0);
        var db = conn.GetDatabase();
        RedisKey key = Me();

        db.KeyDelete(key, CommandFlags.FireAndForget);

        // SET reports the previous value, GET the current one; the #N form indexes by width
        Assert.Equal(0, db.StringBitField(key, BitFieldOperation.Set(BitFieldEncoding.UInt8, BitFieldOffset.Element(1), 255)));
        Assert.Equal(255, await db.StringBitFieldAsync(key, BitFieldOperation.Get(BitFieldEncoding.UInt8, BitFieldOffset.Element(1))));
        Assert.Equal(255, db.StringBitField(key, BitFieldOperation.Get(BitFieldEncoding.UInt8, 8))); // ... same field, by bit
        Assert.Equal(0, db.StringBitField(key, BitFieldOperation.Get(BitFieldEncoding.UInt8, 0)));

        // INCRBY reports the new value
        Assert.Equal(-1, db.StringBitField(key, BitFieldOperation.IncrementBy(BitFieldEncoding.Int8, 0, -1)));
    }

    [Fact]
    public async Task BitFieldBatchAppliesInOrder()
    {
        await using var conn = Create(require: RedisFeatures.v3_2_0);
        var db = conn.GetDatabase();
        RedisKey key = Me();

        db.KeyDelete(key, CommandFlags.FireAndForget);

        using var lease = await db.StringBitFieldAsync(
            key,
            new[]
            {
                BitFieldOperation.Set(BitFieldEncoding.Int8, 0, 100),
                BitFieldOperation.IncrementBy(BitFieldEncoding.Int8, 0, 100, BitFieldOverflow.Saturate),
                BitFieldOperation.Get(BitFieldEncoding.Int8, 0),
                BitFieldOperation.IncrementBy(BitFieldEncoding.Int8, 0, 100, BitFieldOverflow.Fail),
                BitFieldOperation.IncrementBy(BitFieldEncoding.Int8, 0, 100, BitFieldOverflow.Wrap),
            });

        // the FAIL element is null; note the sticky OVERFLOW state survives the intervening GET
        Assert.Equal(new long?[] { 0, 127, 127, null, -29 }, lease.Span.ToArray());
    }

    [Fact]
    public async Task BitFieldEmptyBatchIsANoOp()
    {
        await using var conn = Create(require: RedisFeatures.v3_2_0);
        var db = conn.GetDatabase();
        RedisKey key = Me();

        using var lease = db.StringBitField(key, ReadOnlyMemory<BitFieldOperation>.Empty);
        Assert.True(lease.IsEmpty);

        using var leaseAsync = await db.StringBitFieldAsync(key, ReadOnlyMemory<BitFieldOperation>.Empty);
        Assert.True(leaseAsync.IsEmpty);
    }

    [Fact]
    public async Task BitFieldAllGetGoesOutAsReadOnlyAndReachesAReplica()
    {
        await using var conn = Create(
            require: RedisFeatures.v6_0_0,
            configuration: TestConfig.Current.PrimaryServerAndPort + "," + TestConfig.Current.ReplicaServerAndPort);
        Assert.SkipUnless(
            conn.GetEndPoints().Any(ep => conn.GetServer(ep).IsReplica),
            "No replica in this configuration");

        var db = conn.GetDatabase();
        RedisKey key = Me();
        var session = new ProfilingSession();
        conn.RegisterProfiler(() => session);

        // an all-GET batch is issued as BITFIELD_RO, so a replica will serve it; the value itself is
        // not asserted here - reading from a replica makes that a race with replication
        using var lease = db.StringBitField(
            key,
            new[] { BitFieldOperation.Get(BitFieldEncoding.UInt8, 0) },
            CommandFlags.DemandReplica);
        Assert.Equal(1, lease.Length);
        Assert.NotNull(db.StringBitField(key, BitFieldOperation.Get(BitFieldEncoding.UInt8, 0), CommandFlags.DemandReplica));

        // ... whereas anything that writes stays as BITFIELD
        db.StringBitField(key, BitFieldOperation.Set(BitFieldEncoding.UInt8, 0, 1));

        var commands = session.FinishProfiling().ToList();
        foreach (var command in commands)
        {
            Log($"{command.Command} -> {command.EndPoint}");
        }

        var readOnly = commands.Where(c => c.Command == "BITFIELD_RO").ToList();
        Assert.Equal(2, readOnly.Count);
        Assert.All(readOnly, c => Assert.True(conn.GetServer(c.EndPoint).IsReplica));

        var write = Assert.Single(commands, c => c.Command == "BITFIELD");
        Assert.False(conn.GetServer(write.EndPoint).IsReplica);
    }

    private static void SetSampleBits(IDatabase db, RedisKey key)
    {
        // the sample from redis/redis#15986: bits 1, 7, 9 and 30
        db.KeyDelete(key, CommandFlags.FireAndForget);
        foreach (var bit in new long[] { 1, 7, 9, 30 })
        {
            db.StringSetBit(key, bit, true, CommandFlags.FireAndForget);
        }
    }

    [Fact]
    public async Task StringBitPositions_Ranges()
    {
        await using var conn = Create(require: RedisFeatures.v8_12_0);
        var db = conn.GetDatabase();
        RedisKey key = Me();
        SetSampleBits(db, key);

        Assert.Equal(new long[] { 1, 7, 9, 30 }, db.StringBitPositions(key, true));
        Assert.Equal(new long[] { 9, 30 }, db.StringBitPositions(key, true, 1, -1));
        Assert.Equal(new long[] { 7, 9 }, await db.StringBitPositionsAsync(key, true, 5, 10, StringIndexType.Bit));
        Assert.Empty(db.StringBitPositions(key, true, 3, 1)); // reversed range
    }

    [Fact]
    public async Task StringBitPositions_ClearBits()
    {
        await using var conn = Create(require: RedisFeatures.v8_12_0);
        var db = conn.GetDatabase();
        RedisKey key = Me();
        SetSampleBits(db, key);

        Assert.Equal(new long[] { 24, 25, 26, 27, 28, 29, 31 }, db.StringBitPositions(key, false, 24, 31, StringIndexType.Bit));
        // unlike BITPOS, the range is clamped to the value: nothing past bit 31 is reported
        Assert.Equal(new long[] { 31 }, await db.StringBitPositionsAsync(key, false, 30, 100, StringIndexType.Bit));
    }

    [Fact]
    public async Task StringBitPositions_Limit()
    {
        await using var conn = Create(require: RedisFeatures.v8_12_0);
        var db = conn.GetDatabase();
        RedisKey key = Me();
        SetSampleBits(db, key);

        Assert.Equal(new long[] { 1 }, db.StringBitPositions(key, true, 0, -1, StringIndexType.Bit, limit: 1));
        Assert.Equal(new long[] { 7, 9 }, db.StringBitPositions(key, true, 2, -1, StringIndexType.Bit, limit: 2));
        Assert.Equal(new long[] { 1, 7 }, db.StringBitPositions(key, true, 0, -1, StringIndexType.Byte, limit: 1)); // BYTE completes byte 0
        Assert.Empty(await db.StringBitPositionsAsync(key, true, 0, -1, StringIndexType.Byte, limit: 0));
    }

    [Theory]
    [InlineData(StringIndexType.Bit)]
    [InlineData(StringIndexType.Byte)]
    public async Task StringBitPositions_PagesWithoutGaps(StringIndexType indexType)
    {
        await using var conn = Create(require: RedisFeatures.v8_12_0);
        var db = conn.GetDatabase();
        RedisKey key = Me() + ":" + indexType;

        db.KeyDelete(key, CommandFlags.FireAndForget);
        var rand = new Random(12345);
        for (int i = 0; i < 10_000; i++)
        {
            if (rand.Next(3) == 0) db.StringSetBit(key, i, true, CommandFlags.FireAndForget);
        }
        var all = await db.StringBitPositionsAsync(key, true);
        Assert.NotEmpty(all);

        var paged = new System.Collections.Generic.List<long>();
        long start = 0;
        const int Limit = 97;
        while (true)
        {
            var page = db.StringBitPositions(key, true, start, -1, indexType, limit: Limit);
            paged.AddRange(page);
            if (page.Length < Limit) break;
            long last = page[page.Length - 1];
            start = indexType == StringIndexType.Bit ? last + 1 : (last / 8) + 1;
        }
        Assert.Equal(all, paged);
    }

    [Fact]
    public async Task StringBitPositions_MissingKeyAndWrongType()
    {
        await using var conn = Create(require: RedisFeatures.v8_12_0);
        var db = conn.GetDatabase();
        RedisKey key = Me();

        db.KeyDelete(key, CommandFlags.FireAndForget);
        Assert.Empty(db.StringBitPositions(key, true));

        db.ListRightPush(key, "a", flags: CommandFlags.FireAndForget);
        var ex = await Assert.ThrowsAsync<RedisServerException>(() => db.StringBitPositionsAsync(key, true));
        Assert.Contains("WRONGTYPE", ex.Message);
    }
}
