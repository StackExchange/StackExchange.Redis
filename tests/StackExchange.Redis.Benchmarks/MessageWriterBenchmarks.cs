using System;
using System.Buffers;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;

namespace StackExchange.Redis.Benchmarks;

// Formatting only - no server. Two writers, because they answer different questions:
//
//   Reusable - always returns the remainder of a 16KB buffer, so the hint is always honoured and the
//              fallback branch is never entered. This measures what the CHECK costs on the hot path.
//   Stingy   - never returns more than 8 bytes, so every fixed-size burst takes the fallback. This
//              measures what the fallback costs, and does not run at all before the fix (it throws).
//
// 8 is not arbitrary, and 64 would have been useless: the largest ask in SET user:1 marc is 23 (WriteHeader
// wants 9 framed command bytes + 3 + MaxInt32TextLen; WriteCountPrefix wants 3 + MaxInt64TextLen), so a
// 64-byte cap declines nothing and measures the direct path twice. 16 declines the two 23-byte asks but
// still does not throw on main, whose WriteHeader writes only 13 of the 23 it asked for. 8 is the smallest
// interesting value: every fallback here is exercised, and main faults on commandBytes.CopyTo(span[4..]).
//
// Run on both sides of the change; the numbers are only meaningful as a before/after pair, and only the
// Reusable ones can be compared against a build that predates the fix.
[Config(typeof(CustomConfig))]
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class MessageWriterBenchmarks
{
    private readonly RedisKey _key = "user:1";
    private readonly RedisValue _short = "marc";
    private RedisValue _long;
    private RedisValue[] _several = null!;
    private readonly Reusable _target = new();
    private readonly Stingy _stingy = new();

    [GlobalSetup]
    public void Setup()
    {
        _long = new string('x', 4096);
        _several = ["marc", "EX", 300, 1.5, long.MinValue];
    }

    private MessageWriter Writer => new(null, CommandMap.Default, _target);

    [BenchmarkCategory("KeyValue"), Benchmark]
    public int KeyValue()
    {
        _target.Reset();
        Message.Create(0, CommandFlags.None, RedisCommand.SET, _key, _short).WriteTo(Writer);
        return _target.Written;
    }

    [BenchmarkCategory("Mixed"), Benchmark]
    public int MixedValueShapes()
    {
        _target.Reset();
        Message.Create(0, CommandFlags.None, RedisCommand.SET, _key, _several).WriteTo(Writer);
        return _target.Written;
    }

    [BenchmarkCategory("Large"), Benchmark]
    public int LargeValue()
    {
        _target.Reset();
        Message.Create(0, CommandFlags.None, RedisCommand.SET, _key, _long).WriteTo(Writer);
        return _target.Written;
    }

    [BenchmarkCategory("Fallback"), Benchmark]
    public int KeyValue_ShortSpans()
    {
        _stingy.Reset();
        Message.Create(0, CommandFlags.None, RedisCommand.SET, _key, _short)
               .WriteTo(new MessageWriter(null, CommandMap.Default, _stingy));
        return _stingy.Written;
    }

    /// <summary>Hands out at most 8 bytes at a time, so every fixed-size burst takes the fallback.</summary>
    private sealed class Stingy : IBufferWriter<byte>
    {
        private const int Max = 8;
        private readonly byte[] _buffer = new byte[64 * 1024];

        public int Written { get; private set; }

        public void Reset() => Written = 0;

        public void Advance(int count) => Written += count;

        public Memory<byte> GetMemory(int sizeHint = 0) => new(_buffer, Written, Available);

        public Span<byte> GetSpan(int sizeHint = 0) => new(_buffer, Written, Available);

        private int Available => Math.Min(Max, _buffer.Length - Written);
    }

    /// <summary>A reusable buffer writer, so buffer management is not part of the measurement.</summary>
    private sealed class Reusable : IBufferWriter<byte>
    {
        private readonly byte[] _buffer = new byte[16 * 1024];

        public int Written { get; private set; }

        public void Reset() => Written = 0;

        public void Advance(int count) => Written += count;

        public Memory<byte> GetMemory(int sizeHint = 0) => new(_buffer, Written, _buffer.Length - Written);

        public Span<byte> GetSpan(int sizeHint = 0) => new(_buffer, Written, _buffer.Length - Written);
    }
}
