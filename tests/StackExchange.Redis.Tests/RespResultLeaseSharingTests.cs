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
/// <remarks>
/// <b>Asserted as changes to the count, never as the count itself.</b> A result that shares the receive
/// buffer shares it with whatever else landed in the same read, so the absolute number says how replies
/// happened to pack rather than anything about this result - it was 2 for a 4KB blob and 5 for a two-byte
/// one, and neither is a fact worth pinning. What the sharing contract actually claims is the delta:
/// taking a lease adds exactly one reference (a COPY would add none), disposing it removes exactly one,
/// and disposing it again removes nothing.
/// </remarks>
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
            var baseline = result.RefCount;

            using var lease = result.ReadScalar().ReadLease();
            Assert.NotNull(lease);
            Assert.Equal(baseline + 1, result.RefCount); // the lease shares the buffer; a copy would add none
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
            var baseline = result.RefCount;
            var lease = result.ReadScalar().ReadLease();
            lease!.Dispose();

            Assert.Equal(baseline, result.RefCount);
            Assert.Equal(expected, (string?)result.ReadScalar().ReadRedisValue()); // still readable
        }
    }

    [Fact]
    public async Task DisposingRepeatedlyReleasesOnlyOnce()
    {
        var (conn, result, _) = await GetBlobAsync();
        await using var __ = conn;

        var baseline = result.RefCount;
        var lease = result.ReadScalar().ReadLease();
        Assert.Equal(baseline + 1, result.RefCount);

        lease!.Dispose();
        lease.Dispose();
        lease.Dispose();
        Assert.Equal(baseline, result.RefCount); // three disposals, one release

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
            var baseline = result.RefCount;
            var a = result.ReadScalar().ReadLease();
            var b = result.ReadScalar().ReadLease();
            Assert.Equal(baseline + 2, result.RefCount);

            a!.Dispose();
            Assert.Equal(baseline + 1, result.RefCount);
            Assert.Equal(expected, Encoding.UTF8.GetString(b!.Span)); // unaffected by a's disposal
            b.Dispose();
            Assert.Equal(baseline, result.RefCount);
        }
    }

    [Fact]
    public async Task SharedLeaseStillSupportsArraySegmentConsumers()
    {
        // DecodeString and AsStream reach the backing array internally (MemoryMarshal.TryGetArray), and a
        // SHARED lease is backed by a MemoryManager sitting at a non-zero offset inside the reply - which
        // is the case most at risk of regressing. ReadOnlyLease deliberately exposes no ArraySegment of its
        // own: handing out the array is a way to reach outside the lease, and for shared memory that means
        // into somebody else's data. See design notes 6.16.
        var (conn, result, expected) = await GetBlobAsync();
        await using var _ = conn;
        using (result)
        {
            using var lease = result.ReadScalar().ReadLease();

            Assert.Equal(expected.Length, lease!.Length);
            Assert.Equal(expected, Encoding.UTF8.GetString(lease.Span.ToArray()));
            Assert.Equal(expected, lease.DecodeString());

            using var stream = lease.AsStream(ownsLease: false)!;
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
        var baseline = result.RefCount;
        using var lease = result.ReadScalar().ReadLease();
        Assert.Equal(baseline + 1, result.RefCount); // shared, not copied, even for two bytes
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
        var baseline = result.RefCount;
        using var lease = result.ReadScalar().ReadLease();
        Assert.Same(ReadOnlyLease<byte>.Empty, lease); // the read-only sibling now serves this call site
        Assert.Equal(baseline, result.RefCount); // no reference taken, so nothing to strand
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
    /// <remarks>
    /// <b>The value has to be big enough not to fit in one receive buffer.</b> A lease SHARES the
    /// connection's buffer whenever it can reserve the payload, and copies only when it cannot - so a
    /// small value takes the zero-copy path, rents nothing, and this test measures the wrong thing. It
    /// used 1KiB and passed only because the surface it ran on could not reserve at all; the moment one
    /// could, the assertion failed while the pool wiring was perfectly fine.
    /// </remarks>
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
        const int Size = 256 * 1024; // comfortably more than one receive buffer, so the lease must copy
        await db.HashSetAsync(key, "field", new string('y', Size));

        // Where the rent comes from differs by core and the assertion deliberately does not care: the
        // shipped reader chunks a reply this size and the lease has to assemble a copy, while the new one
        // grows its inbound buffer to hold the frame and hands the lease a window onto it. Either way the
        // memory the reply lives in must come from the pool the caller supplied - which the new core did
        // not do at all, renting every inbound buffer from the default and accepting the setting silently.
        var before = pool.RentCount;
        using var lease = await db.HashGetLeaseAsync(key, "field");
        Assert.NotNull(lease);
        Assert.Equal(Size, lease!.Length);
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
