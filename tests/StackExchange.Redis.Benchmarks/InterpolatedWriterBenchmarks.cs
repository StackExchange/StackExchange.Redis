using System;
using System.Buffers;
using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using StackExchange.Redis.Interpolated;

namespace StackExchange.Redis.Benchmarks;

// Formatting only - no server, no dispatch. The interpolated writer replaces the WRITE half, so this
// compares rendering the same command three ways:
//
//   Message      - the mainstream typed path: Message.Create + WriteTo(MessageWriter). What db.StringSet does.
//   Adhoc        - the ad-hoc string path: ExecuteMessage over object[], via TestHarness. What
//                  IDatabase.Execute(string, ...) does, and what the new string overload competes with.
//   Interpolated - the new path.
//
// All three write into the same pre-allocated IBufferWriter, so the buffer itself is not being measured;
// what differs is the boxing, the intermediate argument arrays, and whether anything is re-materialised.
[Config(typeof(CustomConfig))]
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class InterpolatedWriterBenchmarks
{
    private readonly RedisKey _key = "user:1";
    private readonly RedisValue _value = "marc";
    private readonly Reusable _target = new();
    private RespContext _ctx;
    private object[] _adhocArgs = null!;
    private RedisValue[] _multiValues = null!;

    [GlobalSetup]
    public void Setup()
    {
        _ctx = new RespContext();
        _adhocArgs = new object[] { "user:1", "marc" };
        _multiValues = new RedisValue[] { "marc", "EX", 300 };
    }

    // ---- SET key value -----------------------------------------------------------------------------

    [BenchmarkCategory("KeyValue"), Benchmark(Baseline = true)]
    public int KeyValue_Message()
    {
        _target.Reset();
        var msg = Message.Create(0, CommandFlags.None, RedisCommand.SET, _key, _value);
        msg.WriteTo(new MessageWriter(null, CommandMap.Default, _target));
        return _target.Written;
    }

    [BenchmarkCategory("KeyValue"), Benchmark]
    public int KeyValue_Adhoc()
    {
        _target.Reset();
        var msg = new RedisDatabase.ExecuteMessage(CommandMap.Default, 0, CommandFlags.None, "SET", _adhocArgs);
        msg.WriteTo(new MessageWriter(null, CommandMap.Default, _target));
        return _target.Written;
    }

    [BenchmarkCategory("KeyValue"), Benchmark]
    public int KeyValue_Interpolated()
    {
        using var frame = _ctx.Execute(RedisCommand.SET, $"{_key} {_value}");
        _target.Reset();
        _target.Write(frame.Span);
        return _target.Written;
    }

    // ---- SET key value EX 300 (four arguments) -----------------------------------------------------

    [BenchmarkCategory("Expiry"), Benchmark(Baseline = true)]
    public int Expiry_Message()
    {
        _target.Reset();
        var msg = Message.Create(0, CommandFlags.None, RedisCommand.SET, _key, _multiValues);
        msg.WriteTo(new MessageWriter(null, CommandMap.Default, _target));
        return _target.Written;
    }

    [BenchmarkCategory("Expiry"), Benchmark]
    public int Expiry_Interpolated()
    {
        using var frame = _ctx.Execute(RedisCommand.SET, $"{_key} {_value} {(RedisValue)"EX"} {(RedisValue)300}");
        _target.Reset();
        _target.Write(frame.Span);
        return _target.Written;
    }

    /// <summary>A trivial reusable buffer writer, so buffer management is not part of the measurement.</summary>
    private sealed class Reusable : IBufferWriter<byte>
    {
        private readonly byte[] _buffer = new byte[4096];

        public int Written { get; private set; }

        public void Reset() => Written = 0;

        public void Advance(int count) => Written += count;

        public Memory<byte> GetMemory(int sizeHint = 0) => new(_buffer, Written, _buffer.Length - Written);

        public Span<byte> GetSpan(int sizeHint = 0) => new(_buffer, Written, _buffer.Length - Written);

        public void Write(ReadOnlySpan<byte> value)
        {
            value.CopyTo(new Span<byte>(_buffer, Written, _buffer.Length - Written));
            Written += value.Length;
        }
    }
}
