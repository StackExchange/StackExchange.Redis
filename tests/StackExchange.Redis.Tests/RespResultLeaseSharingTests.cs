using System;
using System.Buffers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Covers the reference-counted sharing between a <see cref="RespResult"/> and the leases taken from it:
/// a contiguous scalar payload should be handed out by reference rather than copied, and the underlying
/// buffer should survive until the result and every lease have been disposed.
/// </summary>
public class RespResultLeaseSharingTests(ITestOutputHelper output, SharedConnectionFixture fixture) : TestBase(output, fixture)
{
    private async Task<(IInternalConnectionMultiplexer Conn, RespResult Result, string Expected)> GetBlobAsync(int size = 4096)
    {
        var conn = Create();
        var db = conn.GetDatabase();
        RedisKey key = Me();
        var expected = new string('x', size - 8) + "-the-end";
        await db.StringSetAsync(key, expected);
        return (conn, await db.ExecuteRespAsync("GET", new RedisKeyOrValue[] { key }), expected);
    }

    [Fact]
    public async Task ReadLease_SharesTheReplyBuffer_RatherThanCopying()
    {
        var (conn, result, expected) = await GetBlobAsync();
        await using var _ = conn;
        using (result)
        {
            Assert.Equal(1, result.RefCount);

            using var lease = result.ReadScalar().ReadLease();
            Assert.NotNull(lease);
            Assert.Equal(2, result.RefCount); // the lease shares the buffer; nothing was copied
            Assert.Equal(expected, Encoding.UTF8.GetString(lease!.Span));
        }
    }

    [Fact]
    public async Task LeaseMayOutliveTheResult()
    {
        var (conn, result, expected) = await GetBlobAsync();
        await using var _ = conn;

        var lease = result.ReadScalar().ReadLease();
        result.Dispose();

        // the buffer is still alive, because the lease still holds a reference
        Assert.Equal(expected, Encoding.UTF8.GetString(lease!.Span));
        lease.Dispose();
        Assert.Throws<ObjectDisposedException>(() => lease.Span.Length);
    }

    [Fact]
    public async Task ResultMayOutliveTheLease()
    {
        var (conn, result, expected) = await GetBlobAsync();
        await using var _ = conn;
        using (result)
        {
            var lease = result.ReadScalar().ReadLease();
            lease!.Dispose();

            Assert.Equal(1, result.RefCount);
            Assert.Equal(expected, (string?)result.ReadScalar().ReadRedisValue()); // still readable
        }
    }

    [Fact]
    public async Task DisposingRepeatedlyReleasesOnlyOnce()
    {
        var (conn, result, _) = await GetBlobAsync();
        await using var __ = conn;

        var lease = result.ReadScalar().ReadLease();
        Assert.Equal(2, result.RefCount);

        lease!.Dispose();
        lease.Dispose();
        lease.Dispose();
        Assert.Equal(1, result.RefCount);

        result.Dispose();
        result.Dispose();
        // fully released; reading now must fault rather than read a recycled buffer
        Assert.Throws<ObjectDisposedException>(() => result.ReadScalar());
    }

    [Fact]
    public async Task TwoLeasesFromOneResultAreIndependent()
    {
        var (conn, result, expected) = await GetBlobAsync();
        await using var _ = conn;
        using (result)
        {
            var a = result.ReadScalar().ReadLease();
            var b = result.ReadScalar().ReadLease();
            Assert.Equal(3, result.RefCount);

            a!.Dispose();
            Assert.Equal(2, result.RefCount);
            Assert.Equal(expected, Encoding.UTF8.GetString(b!.Span)); // unaffected by a's disposal
            b.Dispose();
            Assert.Equal(1, result.RefCount);
        }
    }

    [Fact]
    public async Task SharedLeaseStillSupportsArraySegmentConsumers()
    {
        // DecodeString and AsStream both go via Lease<byte>.ArraySegment; a shared lease is backed by a
        // MemoryManager rather than an array directly, so this is the case most at risk of regressing
        var (conn, result, expected) = await GetBlobAsync();
        await using var _ = conn;
        using (result)
        {
            using var lease = result.ReadScalar().ReadLease();

            var segment = lease!.ArraySegment;
            Assert.True(segment.Offset > 0, "payload should sit at a non-zero offset within the reply");
            Assert.Equal(expected.Length, segment.Count);
            Assert.Equal(expected, Encoding.UTF8.GetString(segment.Array!, segment.Offset, segment.Count));

            Assert.Equal(expected, lease.DecodeString());

            using var stream = lease.AsStream(ownsLease: false);
            using var reader = new System.IO.StreamReader(stream);
            Assert.Equal(expected, reader.ReadToEnd());
        }
    }

    [Fact]
    public async Task ShortPayloadIsAlsoShared()
    {
        var conn = Create();
        await using var _ = conn;
        var db = conn.GetDatabase();
        RedisKey key = Me();
        await db.StringSetAsync(key, "hi");

        using var result = await db.ExecuteRespAsync("GET", new RedisKeyOrValue[] { key });
        using var lease = result.ReadScalar().ReadLease();
        Assert.Equal(2, result.RefCount);
        Assert.Equal("hi", Encoding.UTF8.GetString(lease!.Span));
    }

    [Fact]
    public async Task EmptyPayloadUsesTheSharedEmptyLease_AndTakesNoReference()
    {
        var conn = Create();
        await using var _ = conn;
        var db = conn.GetDatabase();
        RedisKey key = Me();
        await db.StringSetAsync(key, "");

        using var result = await db.ExecuteRespAsync("GET", new RedisKeyOrValue[] { key });
        using var lease = result.ReadScalar().ReadLease();
        Assert.Same(Lease<byte>.Empty, lease);
        Assert.Equal(1, result.RefCount); // no reference taken, so nothing to strand
    }

    [Fact]
    public async Task NullReplyTakesNoLease()
    {
        var conn = Create();
        await using var _ = conn;
        var db = conn.GetDatabase();
        RedisKey key = Me(); // never set

        using var result = await db.ExecuteRespAsync("GET", new RedisKeyOrValue[] { key });
        Assert.True(result.IsNull);
        Assert.Null(result.ReadScalar().ReadLease());
    }

    [Fact]
    public async Task DisposingASharedNullSingletonIsHarmless()
    {
        // the null replies are process-wide singletons on fixed buffers; disposing one must not
        // poison it for every later caller
        var conn = Create();
        await using var _ = conn;
        var db = conn.GetDatabase();
        RedisKey key = Me();

        for (int i = 0; i < 3; i++)
        {
            var result = await db.ExecuteRespAsync("GET", new RedisKeyOrValue[] { key });
            Assert.True(result.IsNull);
            result.Dispose();
            result.Dispose();
        }

        // the singleton must still be readable: its buffer is reached through the same GetSpan path
        // that throws once a counted buffer has been released, so this would fault if we had counted
        // the singleton down to zero along the way
        using var again = await db.ExecuteRespAsync("GET", new RedisKeyOrValue[] { key });
        Assert.True(again.IsNull);
        var reader = again.Read();
        Assert.True(reader.IsNull);
        Assert.Equal(again.Prefix, reader.Prefix);
    }

    /// <summary>
    /// The pool used for a copied lease is no longer passed in - it is resolved from the reader's
    /// services - so this asserts that a configured ResponseBufferPool is still actually honoured.
    /// Without this, losing the service wiring on a lease path would silently fall back to
    /// ArrayPool&lt;byte&gt;.Shared, with nothing failing to say so.
    /// </summary>
    [Fact]
    public async Task ConfiguredResponseBufferPoolIsUsedForCopiedLeases()
    {
        var pool = new CountingMemoryPool();
        var config = ConfigurationOptions.Parse(GetConfiguration());
        config.ResponseBufferPool = pool;
        config.AllowAdmin = true;

        await using var conn = await ConnectionMultiplexer.ConnectAsync(config);
        var db = conn.GetDatabase();
        RedisKey key = Me();
        await db.HashSetAsync(key, "field", new string('y', 1024));

        var before = pool.RentCount;
        using var lease = await db.HashGetLeaseAsync(key, "field");
        Assert.NotNull(lease);
        Assert.Equal(1024, lease!.Length);
        Assert.True(pool.RentCount > before, $"expected the configured pool to be used; rents went {before} -> {pool.RentCount}");
    }

    private sealed class CountingMemoryPool : MemoryPool<byte>
    {
        private int _rentCount;
        public int RentCount => Volatile.Read(ref _rentCount);
        public override int MaxBufferSize => MemoryPool<byte>.Shared.MaxBufferSize;
        public override IMemoryOwner<byte> Rent(int minBufferSize = -1)
        {
            Interlocked.Increment(ref _rentCount);
            return MemoryPool<byte>.Shared.Rent(minBufferSize);
        }
        protected override void Dispose(bool disposing) { }
    }
}
