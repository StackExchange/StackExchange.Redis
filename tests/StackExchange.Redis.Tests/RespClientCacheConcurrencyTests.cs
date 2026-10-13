using System;
using System.Collections.Concurrent;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Caching;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// The cache under concurrent eviction pressure: many callers, a keyspace far larger than the budget,
/// so nearly every store evicts.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the shape a benchmark found rather than a unit test.</b> The single-threaded eviction
/// tests pass, and so do the concurrent tests that fit inside the budget; what breaks is eviction
/// racing eviction, which needs both at once.
/// </para>
/// <para>
/// The assertion is only "nothing threw", which is unusually weak for a test here and is the point:
/// the failure being guarded is an <see cref="ObjectDisposedException"/> from a cache key whose buffer
/// went back to the pool while another thread was still comparing against it. The neighbouring failure
/// - the same race resolving as a false match instead of a throw - would serve the wrong value, which
/// is why the buffer throws rather than reading on.
/// </para>
/// </remarks>
public class RespClientCacheConcurrencyTests
{
    private static readonly RespContext Ctx = new();

    private static RespRequestFrame Get(string key) => Ctx.Render($"{RedisCommand.GET}{(RedisKey)key}");

    private static void Fill(RespClientCache cache, string key)
    {
        var frame = Get(key);
        if (!cache.TryBeginFill(ref frame, 0, out var fill)) return;

        var payload = RespPayload.Create(Encoding.UTF8.GetBytes("$3\r\nabc\r\n"));
        try
        {
            cache.TryComplete(fill, payload);
        }
        finally
        {
            payload.Release();
        }
    }

    private static bool TryRead(RespClientCache cache, string key)
    {
        using var frame = Get(key);
        if (!cache.TryGet(frame.AsLookupKey(), 0, out var payload)) return false;
        payload.Release();
        return true;
    }

    /// <summary>Concurrent stores over a keyspace far larger than the budget.</summary>
    /// <remarks>
    /// The budget is tiny and the keyspace large, so the evictor runs on almost every store and several
    /// threads are inside it at once - which is what the sad path of a cache benchmark does for fifteen
    /// seconds without pausing.
    /// </remarks>
    [Fact]
    public void ConcurrentEvictionDoesNotTouchAReleasedKey()
    {
        // A tiny budget over a tiny keyspace, so the SAME key is evicted and re-stored constantly:
        // that is what the race needs. A stale sampled key only reaches the byte comparison when its
        // hash and length already match a stored one, which in practice means a fresh copy of itself.
        using var cache = new RespClientCache(new CacheOptions { Admission = CacheAdmission.OnFirstMiss, MaxEntries = 4, EvictionSampleSize = 4 });

        var faults = new ConcurrentQueue<Exception>();
        Parallel.For(0, 32, worker =>
        {
            try
            {
                for (var i = 0; i < 20000; i++)
                {
                    Fill(cache, $"k{(worker + i) % 8}");
                }
            }
            catch (Exception ex)
            {
                faults.Enqueue(ex);
            }
        });

        Assert.True(faults.IsEmpty, faults.TryPeek(out var first) ? first.ToString() : "");
    }

    /// <summary>Reads racing the evictor that is releasing the very entry being read.</summary>
    /// <remarks>
    /// A lookup compares its probe against the stored keys in a bucket, and the dictionary's readers are
    /// lock-free: a reader can still be holding a node the evictor has just removed and released. The
    /// hash and length are compared before the bytes, so the collision that matters is the entry being
    /// looked up - which is exactly the one eviction is most likely to be taking.
    /// </remarks>
    [Fact]
    public void ConcurrentReadsDoNotTouchAReleasedKey()
    {
        using var cache = new RespClientCache(new CacheOptions { Admission = CacheAdmission.OnFirstMiss, MaxEntries = 16, EvictionSampleSize = 8 });

        var faults = new ConcurrentQueue<Exception>();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        Parallel.For(0, 16, worker =>
        {
            try
            {
                var reader = (worker & 1) == 0;
                for (var i = 0; i < 4000 && !stop.IsCancellationRequested; i++)
                {
                    var key = $"k{((worker * 4000) + i) % 400}";
                    if (reader) TryRead(cache, key);
                    else Fill(cache, key);
                }
            }
            catch (Exception ex)
            {
                faults.Enqueue(ex);
            }
        });

        Assert.True(faults.IsEmpty, faults.TryPeek(out var first) ? first.ToString() : "");
    }

    /// <summary>Eviction examines a bounded number of candidates, whatever the cache holds.</summary>
    /// <remarks>
    /// <para>
    /// <b>The regression this exists for cost 30x throughput and no test noticed.</b> Eviction used to
    /// sample by choosing a moving offset into the dictionary and walking to it, so evicting one entry
    /// from a 32k-entry cache examined thousands - on every store. Every eviction test passed throughout,
    /// because they all hold a handful of entries.
    /// </para>
    /// <para>
    /// Asserted as a COUNT rather than a duration: a time-based bar measures the machine and fails on a
    /// busy one, where "did the work stay bounded" is exactly the question and is answerable exactly.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(64)]
    [InlineData(20000)]
    public void EvictionWorkDoesNotScaleWithCacheSize(int resident)
    {
        using var cache = new RespClientCache(new CacheOptions { Admission = CacheAdmission.OnFirstMiss, MaxEntries = resident, EvictionSampleSize = 8 });

        for (var i = 0; i < resident; i++) Fill(cache, $"seed{i}");

        var before = cache.EvictionCandidates;
        const int Stores = 500;
        for (var i = 0; i < Stores; i++) Fill(cache, $"probe{i}");

        Assert.Equal(Stores, cache.Evicted); // one eviction per store, so the comparison is like for like

        // a generous ceiling: the window is 8, plus the stale allowance. What it rules out is the walk,
        // which would put this in the millions for the larger cache
        var perEviction = (cache.EvictionCandidates - before) / (double)Stores;
        Assert.True(perEviction <= 64, $"examined {perEviction:F1} candidates per eviction over {resident} entries");
    }
}
