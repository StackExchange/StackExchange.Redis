using System;
using System.Buffers;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Round-trips a multi-segment <see cref="ReadOnlySequence{T}"/>-backed <see cref="RedisValue"/>
/// (<see cref="RedisValue.StorageType.Sequence"/>) through the shared in-process server via
/// StringSet/StringGet, exercising the segmented write path in <c>MessageWriter</c>.
/// </summary>
public class RedisValueSequenceServerTests(ITestOutputHelper output, InProcServerFixture fixture) : TestBase(output, fixture)
{
    // one segment per byte => a genuinely multi-segment sequence (StorageType.Sequence)
    private static RedisValue MultiSegment(byte[] payload)
    {
        var chunks = new ReadOnlyMemory<byte>[payload.Length];
        for (int i = 0; i < payload.Length; i++)
        {
            chunks[i] = new ReadOnlyMemory<byte>(payload, i, 1);
        }
        return FragmentedSegment<byte>.Create(chunks);
    }

    [Fact]
    public async Task StringSet_MultiSegmentSequence_RoundTrips()
    {
        await using var conn = Create();
        var db = conn.GetDatabase();
        var key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);

        var payload = Encoding.UTF8.GetBytes("the quick brown fox jumps over the lazy dog");
        RedisValue value = MultiSegment(payload);
        Assert.Equal(RedisValue.StorageType.Sequence, value.Type);

        Assert.True(db.StringSet(key, value));

        var roundTripped = db.StringGet(key);
        Assert.Equal(payload, (byte[]?)roundTripped);
    }

    [Fact]
    public async Task StringSetAsync_MultiSegmentSequence_RoundTrips()
    {
        await using var conn = Create();
        var db = conn.GetDatabase();
        var key = Me();
        await db.KeyDeleteAsync(key);

        var payload = Encoding.UTF8.GetBytes("a multi-segment sequence payload long enough to span several segments");
        RedisValue value = MultiSegment(payload);
        Assert.Equal(RedisValue.StorageType.Sequence, value.Type);

        Assert.True(await db.StringSetAsync(key, value));

        var roundTripped = await db.StringGetAsync(key);
        Assert.Equal(payload, (byte[]?)roundTripped);
    }
}
