using System;
using System.IO.Hashing;
using System.Security.Cryptography;
using BenchmarkDotNet.Attributes;

namespace StackExchange.Redis.Benchmarks;

/// <summary>
/// The cache key's hash, as it is today against XxHash3 - over the frame sizes actually measured.
/// </summary>
/// <remarks>
/// <para>
/// <b>The cheapest question that decides the work.</b> The cache probe is 31% of a cache hit and grows
/// with key size (32ns at an 8-byte key, 61ns at 256); hashing is one of three things in there, alongside
/// <c>SequenceEqual</c> and an interlocked <c>TryRetain</c>. If hashing is most of it there is a change
/// worth making, and if it is not, nothing downstream matters.
/// </para>
/// <para>
/// The sizes are the frames those keys actually produce, not round numbers: a GET frame is about
/// <c>25 + key</c> bytes.
/// </para>
/// <para>
/// <b>This bounds the win rather than predicting it.</b> A hash that is 20ns faster in isolation can only
/// take 20ns off the probe - the rest of the probe is untouched - which is the arithmetic that earlier
/// microbenchmarks on this branch got wrong in the other direction.
/// </para>
/// </remarks>
[Config(typeof(CustomConfig))]
public class FrameHashBenchmarks
{
    /// <summary>Frame sizes for keys of 8, 64, 96, 128 and 256 bytes.</summary>
    [Params(41, 98, 130, 163, 291)]
    public int FrameBytes { get; set; }

    private byte[] _frame = [];
    private static readonly long Seed = ReadSeed();

    private static long ReadSeed()
    {
        var bytes = new byte[sizeof(long)];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return BitConverter.ToInt64(bytes, 0);
    }

    [GlobalSetup]
    public void Setup()
    {
        _frame = new byte[FrameBytes];
        new Random(42).NextBytes(_frame);
    }

    /// <summary>What the cache key hashes with today: a serial loop with a carried accumulator.</summary>
    [Benchmark(Baseline = true, Description = "current (RedisValue.GetHashCode)")]
    public int Current() => RedisValue.GetHashCode(_frame);

    /// <summary>XxHash3, folded to 32 bits the way the restored comparer folds it.</summary>
    [Benchmark(Description = "XxHash3 + xor-fold")]
    public int XxHash3Folded()
    {
        var hash = XxHash3.HashToUInt64(_frame, Seed);
        return unchecked((int)hash ^ (int)(hash >> 32));
    }

    /// <summary>And unseeded, in case the seed argument costs anything.</summary>
    [Benchmark(Description = "XxHash3 + xor-fold (unseeded)")]
    public int XxHash3Unseeded()
    {
        var hash = XxHash3.HashToUInt64(_frame);
        return unchecked((int)hash ^ (int)(hash >> 32));
    }
}
