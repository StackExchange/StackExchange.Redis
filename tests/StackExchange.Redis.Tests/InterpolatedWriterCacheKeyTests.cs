using System;
using System.Collections.Concurrent;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// The rendered frame as a client-side-cache key: no copy, no string, and a lease that cannot be recycled
/// while anyone is reading it. See design/interpolated-resp-writer.md section 6.4.
/// </summary>
public class InterpolatedWriterCacheKeyTests
{
    private static RespRequest Key(string key)
    {
        var ctx = new RespContext();
        var frame = ctx.Render($"{RedisCommand.GET}{(RedisKey)key}");
        return frame.Detach();   // ownership moves to the key; the frame must not be disposed after this
    }

    private static string Text(ReadOnlySpan<byte> value) =>
        Encoding.UTF8.GetString(value.ToArray()).Replace("\r\n", "|");

    [Fact]
    public void DetachedKeyHoldsTheRenderedFrame()
    {
        using var key = Key("abc");
        Assert.Equal("*2|$3|GET|$3|abc|", Text(key.Span));
    }

    [Fact]
    public void SeparateRendersOfTheSameCommandAreEqual()
    {
        using var a = Key("abc");
        using var b = Key("abc");

        // different rentals, different arrays - equality has to be by content, or the cache never hits
        Assert.False(ReferenceEquals(null, null) && a.Span == b.Span);
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void DifferentCommandsAreNotEqual()
    {
        using var a = Key("abc");
        using var b = Key("abd");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void WorksAsAConcurrentDictionaryKey()
    {
        var cache = new ConcurrentDictionary<RespRequest, RespPayload>();

        var stored = Key("abc");
        var payload = RespPayload.Create(Encoding.UTF8.GetBytes("$5\r\nhello\r\n"));
        Assert.True(cache.TryAdd(stored, payload));

        // a completely separate render finds it
        using (var lookup = Key("abc"))
        {
            Assert.True(cache.TryGetValue(lookup, out var found));
            Assert.True(found.TryRetain());
            try
            {
                Assert.Equal("$5|hello|", Text(found.Span));
            }
            finally
            {
                found.Release();
            }
        }

        Assert.True(cache.TryRemove(stored, out _));
        payload.Dispose();
        stored.Dispose();
    }

    [Fact]
    public void LookupAllocatesNothingOnAHit()
    {
        var cache = new ConcurrentDictionary<RespRequest, RespPayload>();
        var stored = Key("abc");
        var payload = RespPayload.Create(Encoding.UTF8.GetBytes("$5\r\nhello\r\n"));
        cache.TryAdd(stored, payload);

        // the render rents from the pool and returns it, the key is a struct, the payload is already
        // allocated, and RespReader is a ref struct - so a steady-state hit should allocate nothing
        AllocationAssert.None(() => Probe(cache), iterations: 1000, warmup: 200);

        cache.TryRemove(stored, out _);
        payload.Dispose();
        stored.Dispose();

        static void Probe(ConcurrentDictionary<RespRequest, RespPayload> cache)
        {
            // the HIT path borrows rather than detaching: Detach allocates a RefCountedBuffer per call
            var ctx = new RespContext();
            using var frame = ctx.Render($"{RedisCommand.GET}{(RedisKey)"abc"}");
            if (cache.TryGetValue(frame.AsLookupKey(), out var found) && found.TryRetain())
            {
                try
                {
                    if (found.Span.Length == 0) throw new InvalidOperationException();
                }
                finally
                {
                    found.Release();
                }
            }
        }
    }

    [Fact]
    public void ABorrowedKeyCannotBeRetainedAndSoCannotBeStored()
    {
        var ctx = new RespContext();
        using var frame = ctx.Render($"{RedisCommand.GET}{(RedisKey)"abc"}");

        var borrowed = frame.AsLookupKey();
        Assert.False(borrowed.IsOwned);

        // this is the safety property: the documented store idiom is "retain, then add", and a borrowed
        // key refuses to retain - so a pooled array cannot reach a cache by following the idiom
        Assert.False(borrowed.TryRetain(out _));

        using var owned = ctx.Render($"{RedisCommand.GET}{(RedisKey)"abc"}").Detach();
        Assert.True(owned.IsOwned);
        Assert.Equal(borrowed, owned);   // same bytes either way
    }

    [Fact]
    public void DetachAllocatesAndBorrowingDoesNot()
    {
        var ctx = new RespContext();

        var borrowed = AllocationAssert.Measure(
            () =>
            {
                using var frame = ctx.Render($"{RedisCommand.GET}{(RedisKey)"abc"}");
                frame.AsLookupKey();
            },
            iterations: 100,
            warmup: 200);

        var detached = AllocationAssert.Measure(
            () => ctx.Render($"{RedisCommand.GET}{(RedisKey)"abc"}").Detach().Dispose(),
            iterations: 100,
            warmup: 200);

        // this is why AsLookupKey exists: Detach costs one RefCountedBuffer per call, which on a cache HIT
        // buys nothing, because the caller never wanted ownership
        Assert.Equal(0, borrowed);
        Assert.True(detached > 0, "expected Detach to allocate a lease");
    }

    [Fact]
    public void ReadingAfterTheLastReferenceThrows()
    {
        var key = Key("abc");
        key.Dispose();

        // the whole point of RefCountedBuffer being a MemoryManager: this throws rather than quietly
        // reading bytes that now belong to someone else's rent
        Assert.Throws<ObjectDisposedException>(() => key.Span.Length);
    }

    [Fact]
    public void RetainKeepsTheBufferAliveAcrossTheOwnersDispose()
    {
        var key = Key("abc");
        Assert.True(key.TryRetain(out var retained));
        key.Dispose();                        // the original holder is done

        Assert.Equal("*2|$3|GET|$3|abc|", Text(retained.Span));   // still valid: the cache pins it
        retained.Dispose();

        Assert.Throws<ObjectDisposedException>(() => retained.Span.Length);
    }

    [Fact]
    public void TryRetainFailsOnceTheBufferIsGone()
    {
        var payload = RespPayload.Create(Encoding.UTF8.GetBytes("$5\r\nhello\r\n"));
        Assert.True(payload.TryRetain());
        payload.Release();

        payload.Dispose();                    // the creator's reference; count now zero

        // an evicted entry must report a miss, not resurrect a buffer that is back in the pool
        Assert.False(payload.TryRetain());
    }

    [Fact]
    public void EvictionDuringUseDoesNotRecycleTheBuffer()
    {
        var payload = RespPayload.Create(Encoding.UTF8.GetBytes("$5\r\nhello\r\n"));

        Assert.True(payload.TryRetain());     // a reader gets in first
        payload.Dispose();                    // eviction drops the cache's reference underneath it

        Assert.Equal(1, payload.RefCount);
        Assert.Equal("$5|hello|", Text(payload.Span));   // the reader is still safe

        payload.Release();
        Assert.Equal(0, payload.RefCount);
        Assert.Throws<ObjectDisposedException>(() => payload.Span.Length);
    }

    [Fact]
    public async Task ConcurrentReadersAndOneEvictionNeverTearOrOverRelease()
    {
        for (var round = 0; round < 200; round++)
        {
            var payload = RespPayload.Create(Encoding.UTF8.GetBytes("$5\r\nhello\r\n"));
            var start = new ManualResetEventSlim(false);
            var hits = 0;

            var readers = new Task[8];
            for (var i = 0; i < readers.Length; i++)
            {
                readers[i] = Task.Run(() =>
                {
                    start.Wait();
                    if (payload.TryRetain())
                    {
                        try
                        {
                            // if eviction could recycle under us, this is where it would show
                            Assert.Equal("$5|hello|", Text(payload.Span));
                            Interlocked.Increment(ref hits);
                        }
                        finally
                        {
                            payload.Release();
                        }
                    }
                });
            }

            var evictor = Task.Run(() => { start.Wait(); payload.Dispose(); });

            start.Set();
            await Task.WhenAll(readers);
            await evictor;

            // whatever the interleaving: every retain that succeeded saw intact bytes, and the count
            // lands at exactly zero - no leak, no over-release
            Assert.Equal(0, payload.RefCount);
            Assert.True(hits >= 0);
        }
    }
}
