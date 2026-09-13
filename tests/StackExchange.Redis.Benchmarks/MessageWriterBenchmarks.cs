using System;
using System.Buffers;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;

namespace StackExchange.Redis.Benchmarks;

// Formatting only - no server. Exists to check that verifying GetSpan's size hint did not cost anything on
// the path that matters: the writer's own span is still used directly whenever it is long enough, so the
// added work should be one length comparison per burst plus a stackalloc that SkipLocalsInit makes free.
//
// Run this on both sides of the change; the numbers are only meaningful as a before/after pair.
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
