using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using BenchmarkDotNet.Attributes;

namespace StackExchange.Redis.Benchmarks;

[Config(typeof(CustomConfig))]
public class FastHashBenchmarks
{
    private const string SharedString = "some-typical-data-for-comparisons";
    private static readonly byte[] SharedUtf8;
    private static readonly ReadOnlySequence<byte> SharedMultiSegment;

    static FastHashBenchmarks()
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

#pragma warning disable CS0618 // Type or member is obsolete
        var bytes = _sourceBytes.Span;
        var expected = FastHash.Hash64Fallback(bytes);

        Assert(bytes.HashCS(), nameof(FastHash.HashCS));
        Assert(FastHash.Hash64Unsafe(bytes), nameof(FastHash.Hash64Unsafe));
#pragma warning restore CS0618 // Type or member is obsolete
        Assert(SingleSegmentBytes.Hash64(), nameof(FastHash.HashCS) + " (single segment)");
        Assert(_sourceMultiSegmentBytes.Hash64(), nameof(FastHash.HashCS) + " (multi segment)");

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

    public IEnumerable<int> Sizes => [0, 1, 2, 3, 4, 5, 6, 7, 8, 16];

    private const int OperationsPerInvoke = 1024;

    [Benchmark(OperationsPerInvoke = OperationsPerInvoke, Baseline = true)]
    public void String()
    {
        var val = _sourceString;
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
            _ = val.GetHashCode();
        }
    }

    [Benchmark(OperationsPerInvoke = OperationsPerInvoke)]
    public void Hash64()
    {
        var val = _sourceBytes.Span;
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
            _ = val.HashCS();
        }
    }

    [Benchmark(OperationsPerInvoke = OperationsPerInvoke)]
    public void Hash64Unsafe()
    {
        var val = _sourceBytes.Span;
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
#pragma warning disable CS0618 // Type or member is obsolete
            _ = FastHash.Hash64Unsafe(val);
#pragma warning restore CS0618 // Type or member is obsolete
        }
    }

    [Benchmark(OperationsPerInvoke = OperationsPerInvoke)]
    public void Hash64Fallback()
    {
        var val = _sourceBytes.Span;
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
#pragma warning disable CS0618 // Type or member is obsolete
            _ = FastHash.Hash64Fallback(val);
#pragma warning restore CS0618 // Type or member is obsolete
        }
    }

    [Benchmark(OperationsPerInvoke = OperationsPerInvoke)]
    public void Hash64_SingleSegment()
    {
        var val = SingleSegmentBytes;
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
            _ = val.Hash64();
        }
    }

    [Benchmark(OperationsPerInvoke = OperationsPerInvoke)]
    public void Hash64_MultiSegment()
    {
        var val = _sourceMultiSegmentBytes;
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
            _ = val.Hash64();
        }
    }
}
