using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Caching;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Entry lifetime: the backstop that stops an un-invalidated entry being served forever, and the
/// per-caller freshness requirement that can ask for less than it.
/// </summary>
public class RespCacheLifetimeTests
{
    private const CommandFlags Readable = CommandFlags.CommandRetryReadOnly;

    private static ValueTask<RedisValue> Get(RespDatabaseContext context)
        => context.SendAsync<RedisValue>($"{RedisCommand.GET}{(RedisKey)"k"}", Readable);

    [Fact]
    public async Task TheDefaultLifetimeIsFiniteRatherThanForever()
    {
        // the whole point: with no invalidation wired, an entry with no lifetime is PERMANENTLY stale,
        // which is strictly worse than briefly over-stale
        Assert.True(CachePolicy.Default.TimeToLive < TimeSpan.MaxValue);
        Assert.True(CachePolicy.Default.TimeToLive > TimeSpan.Zero);

        // and a fresh entry is genuinely served from cache under it
        using var cache = new RespClientCache(new CacheOptions { Admission = CacheAdmission.OnFirstMiss });
        var executor = new FakeExecutor("$1\r\na\r\n", "$1\r\nb\r\n");
        var context = new RespDatabaseContext(new RespContext().WithExecutor(executor).WithCache(cache));

        Assert.Equal("a", await Get(context));
        Assert.Equal("a", await Get(context));
        Assert.Equal(1, executor.Sends);
    }

    [Fact]
    public async Task AnExpiredEntryIsNotServed()
    {
        using var cache = new RespClientCache(new CacheOptions { Admission = CacheAdmission.OnFirstMiss, DefaultPolicy = new CachePolicy { TimeToLive = TimeSpan.FromMilliseconds(80) } });
        var executor = new FakeExecutor("$1\r\na\r\n", "$1\r\nb\r\n");
        var context = new RespDatabaseContext(new RespContext().WithExecutor(executor).WithCache(cache));

        Assert.Equal("a", await Get(context));
        Assert.Equal("a", await Get(context));    // still fresh
        Assert.Equal(1, executor.Sends);

        await Task.Delay(200);

        Assert.Equal("b", await Get(context));    // outlived its welcome; fetched again
        Assert.Equal(2, executor.Sends);
        Assert.True(cache.Expired > 0);
    }

    [Fact]
    public async Task AnExpiredEntryIsReplacedByTheNextFill()
    {
        // an expired entry is DEAD for every purpose except the dictionary slot it still occupies; the fill
        // that follows the miss has to take that slot over, not be written off as redundant against it.
        // Otherwise the key cannot be re-cached until the sweep (ten seconds by default) removes the corpse,
        // and a steadily-read key misses on every read for the whole of that gap (#3318). Sweeping is
        // turned off here so that nothing but the fill path can make this pass.
        using var cache = new RespClientCache(new CacheOptions
        {
            Admission = CacheAdmission.OnFirstMiss,
            SweepInterval = TimeSpan.Zero,
            DefaultPolicy = new CachePolicy { TimeToLive = TimeSpan.FromMilliseconds(100) },
        });
        var executor = new FakeExecutor("$1\r\na\r\n", "$1\r\nb\r\n", "$1\r\nc\r\n");
        var context = new RespDatabaseContext(new RespContext().WithExecutor(executor).WithCache(cache));

        Assert.Equal("a", await Get(context));
        Assert.Equal(1, cache.Stored);

        await Task.Delay(200);

        Assert.Equal("b", await Get(context));    // expired; fetched again...
        Assert.Equal(2, executor.Sends);
        Assert.True(cache.Expired > 0);
        Assert.Equal(2, cache.Stored);            // ...and the fresh reply took over the slot
        Assert.Equal(1, cache.Count);

        Assert.Equal("b", await Get(context));    // so the next read is a hit again
        Assert.Equal(2, executor.Sends);
    }

    [Fact]
    public async Task AContextCanDemandSomethingFresherThanThePolicy()
    {
        // the one knob that is per-call, because freshness tolerance is a property of the caller - and the
        // one that could not be added to IDatabase at all without a binary break
        using var cache = new RespClientCache(new CacheOptions { Admission = CacheAdmission.OnFirstMiss, DefaultPolicy = new CachePolicy { TimeToLive = TimeSpan.FromHours(1) } });
        var executor = new FakeExecutor("$1\r\na\r\n", "$1\r\nb\r\n");
        var relaxed = new RespDatabaseContext(new RespContext().WithExecutor(executor).WithCache(cache));
        var picky = relaxed.WithMaxCacheAge(TimeSpan.FromMilliseconds(50));

        Assert.Equal("a", await Get(relaxed));
        await Task.Delay(150);

        // the relaxed caller is still happy with it...
        Assert.Equal("a", await Get(relaxed));
        Assert.Equal(1, executor.Sends);

        // ...and the picky one is not
        Assert.Equal("b", await Get(picky));
        Assert.Equal(2, executor.Sends);
    }

    [Fact]
    public async Task OneEntryServesCallersWithDifferentTolerances()
    {
        // age is applied on READ, not stamped on store - so a single entry serves everybody, rather than
        // being duplicated once per distinct lifetime
        using var cache = new RespClientCache(new CacheOptions { Admission = CacheAdmission.OnFirstMiss, DefaultPolicy = new CachePolicy { TimeToLive = TimeSpan.FromHours(1) } });
        var executor = new FakeExecutor("$1\r\na\r\n");
        var relaxed = new RespDatabaseContext(new RespContext().WithExecutor(executor).WithCache(cache));
        var picky = relaxed.WithMaxCacheAge(TimeSpan.FromMinutes(30));

        Assert.Equal("a", await Get(relaxed));
        Assert.Equal("a", await Get(picky));

        Assert.Equal(1, executor.Sends);
        Assert.Equal(1, cache.Count);   // ONE entry, not one per tolerance
    }

    [Fact]
    public async Task AContextCannotAskForStalerThanThePolicyAllows()
    {
        // narrows, never widens: the deployment's lifetime is a ceiling
        using var cache = new RespClientCache(new CacheOptions { Admission = CacheAdmission.OnFirstMiss, DefaultPolicy = new CachePolicy { TimeToLive = TimeSpan.FromMilliseconds(80) } });
        var executor = new FakeExecutor("$1\r\na\r\n", "$1\r\nb\r\n");
        var context = new RespDatabaseContext(new RespContext().WithExecutor(executor).WithCache(cache)
            .WithMaxCacheAge(TimeSpan.FromHours(1)));

        Assert.Equal("a", await Get(context));
        await Task.Delay(200);

        Assert.Equal("b", await Get(context));
        Assert.Equal(2, executor.Sends);
    }
}
