using System;
using System.Collections.Generic;
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

    // key shapes common in the wild, not one benchmark's: a shared prefix of every length 0-9 (so the varying part
    // falls at every alignment within an 8-byte word), padded and unpadded numbers, a suffix after the number, GUIDs
    public static TheoryData<string> KeyShapes() => new()
    {
        "{0}", "a:{0}", "ab:{0}", "abc:{0}", "user:{0}", "rf:cbig:{0}", "session:{0}", "products:{0}", "inventory:{0}",
        "{0:D8}", "key:{0:D10}", "user:{0}:profile", "guid",
    };

    private static string Key(string shape, int i)
        => shape == "guid" ? new Guid(i, (short)(i >> 7), (short)0x4123, 0x8a, 0x1f, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77).ToString() : string.Format(shape, i);

    // GET {key} as it goes on the wire: every request shares its framing, and differs only where the key does
    private static byte[] Request(string key)
        => System.Text.Encoding.ASCII.GetBytes($"*2\r\n$3\r\nGET\r\n${key.Length}\r\n{key}\r\n");

    [Theory]
    [MemberData(nameof(KeyShapes))]
    public void SimilarKeysAndRequestsHashApart(string shape)
    {
        // the old multiply-by-33 hash gave 200,000 "rf:cbig:{0}" requests ~42,000 hashes between them
        const int Count = 200_000;
        HashSet<int> keys = [], requests = [];
        for (var i = 0; i < Count; i++)
        {
            var key = Key(shape, i);
            keys.Add(RedisValue.GetHashCode(System.Text.Encoding.ASCII.GetBytes(key)));
            requests.Add(RedisValue.GetHashCode(Request(key)));
        }

        // a good 32-bit hash collides ~5 times in 200,000
        Assert.True(keys.Count > Count - 50, $"{keys.Count} distinct key hashes");
        Assert.True(requests.Count > Count - 50, $"{requests.Count} distinct request hashes");
    }

    [Theory]
    [MemberData(nameof(KeyShapes))]
    public void SimilarRequestsAreNotMistakenForRepeats(string shape)
    {
        // first sightings, short of a clear: a Bloom filter this full admits a few percent early. Before the probes
        // were mixed, 200,000 "rf:cbig:{0}" requests landed on ~4,500 bits and nearly every first miss read as a repeat
        var doorkeeper = new CacheDoorkeeper(expectedEntries: 131_072); // a 32 MiB budget's sizing
        var repeats = 0;
        for (var i = 0; i < 100_000; i++)
        {
            if (doorkeeper.SeenBefore(RedisValue.GetHashCode(Request(Key(shape, i))))) repeats++;
        }

        Assert.True(repeats < 5_000, $"{repeats} of 100,000 first sightings read as repeats");
    }

    [Fact]
    public void TheDoorkeeperForgetsOnceFull()
    {
        // the smallest filter is 4 Ki bits and clears after an eighth of that: 512 misses asked about
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

    [Fact]
    public void ASmallKeyspaceIsStillForgotten()
    {
        // repeats age the filter too: counting only first sightings, a keyspace smaller than the window filled it
        // once and was then remembered forever - every later miss a "repeat", admission OnFirstMiss for good
        var doorkeeper = new CacheDoorkeeper(expectedEntries: 1); // clears after 512 misses
        for (var i = 0; i < 100; i++) doorkeeper.SeenBefore(unchecked(i * 104_729)); // 100 keys, each seen once
        Assert.True(doorkeeper.SeenBefore(0));

        var forgotten = false;
        for (var pass = 0; pass < 10 && !forgotten; pass++)
        {
            for (var i = 0; i < 100; i++)
            {
                if (!doorkeeper.SeenBefore(unchecked(i * 104_729))) forgotten = true;
            }
        }

        Assert.True(forgotten, "the same 100 keys, missing over and over, were never forgotten");
    }
}
