using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using BenchmarkDotNet.Attributes;
using RESPite;

namespace StackExchange.Redis.Benchmarks;

// [Config(typeof(CustomConfig))]
[ShortRunJob, MemoryDiagnoser]
public class AsciiHashBenchmarks
{
    private const string SharedString = "some-typical-data-for-comparisons-that-needs-to-be-at-least-64-characters";
    private static readonly byte[] SharedUtf8;
    private static readonly ReadOnlySequence<byte> SharedMultiSegment;

    static AsciiHashBenchmarks()
    {
        SharedUtf8 = Encoding.UTF8.GetBytes(SharedString);

        var first = new Segment(SharedUtf8.AsMemory(0, 1), null);
        var second = new Segment(SharedUtf8.AsMemory(1), first);
        SharedMultiSegment = new ReadOnlySequence<byte>(first, 0, second, second.Memory.Length);
    }

    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        public Segment(ReadOnlyMemory<byte> memory, Segment? previous)
        {
            Memory = memory;
            if (previous is { })
            {
                RunningIndex = previous.RunningIndex + previous.Memory.Length;
                previous.Next = this;
            }
        }
    }

    private string _sourceString = SharedString;
    private ReadOnlyMemory<byte> _sourceBytes = SharedUtf8;
    private ReadOnlySequence<byte> _sourceMultiSegmentBytes = SharedMultiSegment;
    private ReadOnlySequence<byte> SingleSegmentBytes => new(_sourceBytes);

    [GlobalSetup]
    public void Setup()
    {
        _sourceString = SharedString.Substring(0, Size);
        _sourceBytes = SharedUtf8.AsMemory(0, Size);
        _sourceMultiSegmentBytes = SharedMultiSegment.Slice(0, Size);

        var bytes = _sourceBytes.Span;
        var expected = AsciiHash.HashCS(bytes);

        Assert(AsciiHash.HashCS(bytes), nameof(AsciiHash.HashCS) + ":byte");
        Assert(AsciiHash.HashCS(_sourceString.AsSpan()), nameof(AsciiHash.HashCS) + ":char");

        Assert(AsciiHash.HashCS(SingleSegmentBytes), nameof(AsciiHash.HashCS) + " (single segment)");
        Assert(AsciiHash.HashCS(_sourceMultiSegmentBytes), nameof(AsciiHash.HashCS) + " (multi segment)");

        void Assert(long actual, string name)
        {
            if (actual != expected)
            {
                throw new InvalidOperationException($"Hash mismatch for {name}, {expected} != {actual}");
            }
        }
    }

    [ParamsSource(nameof(Sizes))]
    public int Size { get; set; } = 7;

    public IEnumerable<int> Sizes => [0, 1, 2, 3, 4, 5, 6, 7, 8, 16, 64];

    private const int OperationsPerInvoke = 1024;

    // [Benchmark(OperationsPerInvoke = OperationsPerInvoke, Baseline = true)]
    public void StringGetHashCode()
    {
        var val = _sourceString;
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
            _ = val.GetHashCode();
        }
    }

    [BenchmarkCategory("byte")]
    [Benchmark(OperationsPerInvoke = OperationsPerInvoke)]
    public void HashCS_B()
    {
        var val = _sourceBytes.Span;
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
            _ = AsciiHash.HashCS(val);
        }
    }

    [BenchmarkCategory("char")]
    [Benchmark(OperationsPerInvoke = OperationsPerInvoke)]
    public void HashCS_C()
    {
        var val = _sourceString.AsSpan();
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
#pragma warning disable CS0618 // Type or member is obsolete
            _ = AsciiHash.HashCS(val);
#pragma warning restore CS0618 // Type or member is obsolete
        }
    }

    // [Benchmark(OperationsPerInvoke = OperationsPerInvoke)]
    public void Hash64_SingleSegment()
    {
        var val = SingleSegmentBytes;
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
            _ = AsciiHash.HashCS(val);
        }
    }

    // [Benchmark(OperationsPerInvoke = OperationsPerInvoke)]
    public void Hash64_MultiSegment()
    {
        var val = _sourceMultiSegmentBytes;
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
            _ = AsciiHash.HashCS(val);
        }
    }
}
