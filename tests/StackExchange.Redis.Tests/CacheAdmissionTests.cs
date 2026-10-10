using StackExchange.Redis.Caching;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// <see cref="CacheAdmission.OnRepeatedMiss"/>: a first miss is remembered, not stored, and only a repeated miss is
/// cached - TinyLFU's doorkeeper.
/// </summary>
public class CacheAdmissionTests
{
    private static RespDatabaseContext Via(RespExecutorBase executor, RespClientCache cache)
        => new(new RespContext().WithExecutor(executor).WithCache(cache));

    private static string? Read(RespExecutorBase executor, RespClientCache cache, string key)
        => Via(executor, cache).Raw.Send<RedisValue>($"{RedisCommand.GET}{(RedisKey)key}", CommandFlags.CommandRetryReadOnly);

    [Fact]
    public void TheDefaultIsRepeatedMiss()
    {
        // "no opinion", resolving to OnRepeatedMiss today: a first miss is not stored
        using var cache = new RespClientCache();
        var executor = new FakeExecutor("$5\r\nhello\r\n");

        Read(executor, cache, "k");

        Assert.Equal(CacheAdmission.Default, cache.Options.Admission);
        Assert.Equal(CacheAdmission.OnRepeatedMiss, cache.Options.ResolvedAdmission);
        Assert.Equal(0, cache.Count);
        Assert.Equal(1, cache.RefusedNotAdmitted);
    }

    [Fact]
    public void OnFirstMissStoresOnTheFirstMiss()
    {
        using var cache = new RespClientCache(new CacheOptions { Admission = CacheAdmission.OnFirstMiss });
        var executor = new FakeExecutor("$5\r\nhello\r\n");

        Read(executor, cache, "k");
        Read(executor, cache, "k");

        Assert.Equal(1, executor.Sends); // the second read was a hit
        Assert.Equal(0, cache.RefusedNotAdmitted);
    }

    [Fact]
    public void ARepeatedMissIsStoredAndAFirstIsNot()
    {
        using var cache = new RespClientCache(new CacheOptions { Admission = CacheAdmission.OnRepeatedMiss });
        var executor = new FakeExecutor("$5\r\nhello\r\n");

        Assert.Equal("hello", Read(executor, cache, "k"));
        Assert.Equal(0, cache.Count);                  // remembered, not stored
        Assert.Equal(1, cache.RefusedNotAdmitted);

        Assert.Equal("hello", Read(executor, cache, "k"));
        Assert.Equal(1, cache.Count);                  // missed again: now it is cached

        Assert.Equal("hello", Read(executor, cache, "k"));
        Assert.Equal(2, executor.Sends);               // ...and the third read was a hit
        Assert.Equal(1, cache.RefusedNotAdmitted);
    }

    [Fact]
    public void RequestsAreRememberedApart()
    {
        // a first miss on one key admits nothing for another
        using var cache = new RespClientCache(new CacheOptions { Admission = CacheAdmission.OnRepeatedMiss });
        var executor = new FakeExecutor("$5\r\nhello\r\n");

        Read(executor, cache, "a");
        Read(executor, cache, "b");

        Assert.Equal(0, cache.Count);
        Assert.Equal(2, cache.RefusedNotAdmitted);
    }

    [Fact]
    public void TheDoorkeeperHasNoFalseNegatives()
    {
        // whatever it has recorded since the last clear it must recognise: a hot request is never kept out. (The
        // first pass is not asserted: a Bloom filter may report a first sighting as a repeat - a false positive, which
        // only admits early - but must never report a repeat as new.)
        var doorkeeper = new CacheDoorkeeper(expectedEntries: 1_000);
        for (var i = 0; i < 500; i++) doorkeeper.SeenBefore(i * 7919);
        for (var i = 0; i < 500; i++) Assert.True(doorkeeper.SeenBefore(i * 7919), "a repeat not recognised");
    }

    [Fact]
    public void TheDoorkeeperForgetsOnceFull()
    {
        // the smallest filter is 4 Ki bits and clears after an eighth of that: 512 first sightings. Counted rather
        // than assumed, because a false positive is not recorded - so stop at the clear itself
        var doorkeeper = new CacheDoorkeeper(expectedEntries: 1);
        Assert.False(doorkeeper.SeenBefore(42));
        for (var i = 1; i < 10_000; i++)
        {
            doorkeeper.SeenBefore(unchecked(i * 104_729));
            if (doorkeeper.Recorded == 0) break; // just cleared
        }

        Assert.Equal(0, doorkeeper.Recorded);
        Assert.False(doorkeeper.SeenBefore(42)); // forgotten: a first sighting again
    }
}
