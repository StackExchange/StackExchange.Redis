using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using StackExchange.Redis.Caching;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// What the cache does when the keyspace it is caching is also being written.
/// </summary>
/// <remarks>
/// <para>
/// Found by the RespFest cache league: on its `cache-churn-conc64` scenario - 1 KiB GETs over a
/// 1000-key space with one operation in ten a SET, 64 concurrent callers - this cache served
/// <b>0.0%</b> of reads from memory, measured at the server, while every other client in the league
/// served 58-82%. It was not slower; it was absent. The cached build was measurably slower than the
/// same build with caching switched off, which is what a cache that never hits costs.
/// </para>
/// <para>
/// The arithmetic says a correct cache should hit here. At that rate a given key is read roughly
/// every 1.6ms and written roughly every 14.6ms, so about nine reads separate consecutive writes and
/// only the first of them has to miss - an expected hit rate near 89%, which is about what the other
/// clients measured. So 0% is this cache's bug rather than the others' staleness.
/// </para>
/// </remarks>
[Collection(NonParallelCollection.Name)]
public class RespCacheChurnTests(ITestOutputHelper output) : TestBase(output)
{
    private async Task<(ConnectionMultiplexer Muxer, RespClientCache Cache)> TrackedAsync(CacheOptions options)
    {
        var config = new ConfigurationOptions
        {
            EndPoints = { { TestConfig.Current.PrimaryServer, TestConfig.Current.PrimaryPort } },
            Protocol = RedisProtocol.Resp3,
            ClientCache = options,
            AllowAdmin = true,
        };

        ConnectionMultiplexer muxer;
        try
        {
            muxer = await ConnectionMultiplexer.ConnectAsync(config, Writer);
        }
        catch (Exception ex)
        {
            Assert.Skip("Unable to connect to server: " + ex.Message);
            throw;
        }

        var cache = muxer.ClientCache;
        Assert.NotNull(cache);
        return (muxer, cache);
    }

    /// <param name="shape">
    /// Which <see cref="CacheOptions"/> to use. "scoped" declares a prefix, as the suite's other cache
    /// tests do; "unscoped" declares none, so under BCAST the server announces every key anyone writes;
    /// "benchmark" is exactly what the RespFest entry configures. Whichever of these behaves differently
    /// is the finding.
    /// </param>
    [Theory]
    [InlineData("scoped", false, 16, 200)]
    [InlineData("unscoped", false, 16, 200)]
    [InlineData("benchmark", false, 16, 200)]
    // the benchmark's own surface and scale, bisected one dimension at a time
    [InlineData("benchmark", true, 16, 200)]
    [InlineData("benchmark", true, 64, 200)]
    [InlineData("benchmark", true, 64, 1000)]
    [InlineData("benchmark", false, 64, 1000)]
    public async Task ChurnDoesNotDisableTheCache(string shape, bool viaContext, int callers, int keyCount)
    {
        var me = Me() + shape + Guid.NewGuid().ToString("N");
        var (muxer, cache) = await TrackedAsync(shape switch
        {
            "scoped" => new CacheOptions { Admission = CacheAdmission.OnFirstMiss, Prefixes = [me] },
            // exactly what the RespFest entry configures, so a difference here is the difference
            "benchmark" => new CacheOptions
            {
                Admission = CacheAdmission.OnFirstMiss,
                Enabled = true,
                MaxBytes = 32L * 1024 * 1024,
                DefaultPolicy = new CachePolicy { TimeToLive = TimeSpan.FromHours(1) },
            },
            _ => new CacheOptions { Admission = CacheAdmission.OnFirstMiss },
        });
        using var _ = muxer;

        // The benchmark reads through GetDatabaseContext().Strings, not IDatabase.Strings. Both end up
        // at the same group methods, but only one of them is what actually posted 0%.
        var db = viaContext ? muxer.GetDatabaseContext().Strings : muxer.GetDatabase().Strings;
        int Keys = keyCount, Callers = callers;
        const int PerCaller = 1500;
        var payload = new string('x', 1024);
        var keys = Enumerable.Range(0, Keys).Select(i => (RedisKey)$"{me}:{i}").ToArray();

        foreach (var key in keys) await db.SetAsync(key, payload);
        foreach (var key in keys) await db.GetAsync(key); // warm

        // The league measures hit rate at the SERVER - an operation answered from cache is one the
        // server never sees - precisely so a client cannot overstate itself. Do the same here, rather
        // than deriving it from counters whose meaning has to be assumed.
        using var admin = await ConnectionMultiplexer.ConnectAsync(
            TestConfig.Current.PrimaryServerAndPort + ",allowAdmin=true", Writer);
        var server = admin.GetServer(admin.GetEndPoints()[0]);
        async Task<long> CommandsAsync()
        {
            var info = await server.InfoAsync("stats");
            var stat = info.SelectMany(g => g).First(p => p.Key == "total_commands_processed");
            return long.Parse(stat.Value);
        }

        var storedBefore = cache.Stored;
        var commandsBefore = await CommandsAsync();
        var reads = 0L;

        await Task.WhenAll(Enumerable.Range(0, Callers).Select(caller => Task.Run(async () =>
        {
            var position = caller;
            for (var n = 0; n < PerCaller; n++)
            {
                var key = keys[position % Keys];
                position += Callers;

                // one op in ten rewrites the key it touches, as the scenario does
                if (n % 10 == 9)
                {
                    await db.SetAsync(key, payload);
                }
                else
                {
                    await db.GetAsync(key);
                    System.Threading.Interlocked.Increment(ref reads);
                }
            }
        })));

        var fills = cache.Stored - storedBefore;
        var writes = (long)Callers * PerCaller / 10;
        // Every write must reach the server; only reads can be served locally. Anything above the
        // write count is a read that went to the server, so the rest were served from cache.
        var commands = await CommandsAsync() - commandsBefore - 1; // -1 for the INFO itself
        var readsToServer = Math.Max(0, commands - writes);
        var hits = reads - readsToServer;

        Log($"shape={shape} ctx={viaContext} callers={callers} keys={keyCount} reads={reads} writes={writes}");
        Log($"  serverCommands={commands} readsToServer={readsToServer} hits={hits} ({100.0 * hits / reads:F1}%) fills={fills}");
        Log($"  stored={cache.Stored} refreshes={cache.Refreshes} raced={cache.RefusedRaced} notTracked={cache.RefusedNotTracked} flags={cache.RefusedByFlags}");
        Log($"  noKeys={cache.RefusedNoKeys} error={cache.RefusedError} tooLarge={cache.RefusedTooLarge}");
        Log($"  count={cache.Count} bytes={cache.Bytes} evicted={cache.Evicted} expired={cache.Expired}");
        Log($"  servedStale={cache.ServedStale} coalesced={cache.Coalesced} redundant={cache.RedundantFills}");
        Log($"  trackedKeys={cache.TrackedKeyCount} inFlight={cache.InFlightCount}");

        // Deliberately far below the ~89% the arithmetic predicts: this is asserting that the cache
        // does something at all under write traffic, not that it is optimal.
        Assert.True(hits > reads / 4, $"cache served {100.0 * hits / reads:F1}% of {reads} reads under churn");
    }
}
