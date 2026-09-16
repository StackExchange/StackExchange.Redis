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
}
