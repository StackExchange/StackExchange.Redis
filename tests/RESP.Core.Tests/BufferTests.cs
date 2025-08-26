using System;
using System.Buffers;
using System.Diagnostics;
using System.Threading;
using System.Xml.XPath;
using Resp;
using Xunit;

namespace RESP.Core.Tests;

public class BufferTests
{
    [Fact]
    public void SimpleUsage()
    {
        CycleBuffer buffer = CycleBuffer.Create();
        Assert.True(buffer.CommittedIsEmpty);
        Assert.Equal(0, buffer.GetCommittedLength());
        Assert.False(buffer.TryGetFirstCommittedSpan(false, out _));

        buffer.Write("hello world"u8);
        Assert.False(buffer.CommittedIsEmpty);
        Assert.Equal(11, buffer.GetCommittedLength());

        Assert.False(buffer.TryGetFirstCommittedSpan(true, out _));
        Assert.True(buffer.TryGetFirstCommittedSpan(false, out var committed));
        Assert.True(committed.SequenceEqual("hello world"u8));
        buffer.DiscardCommitted(11);
        Assert.True(buffer.CommittedIsEmpty);
        Assert.Equal(0, buffer.GetCommittedLength());
        Assert.False(buffer.TryGetFirstCommittedSpan(false, out _));

        // now partial consume
        buffer.Write("partial consume"u8);
        Assert.False(buffer.CommittedIsEmpty);
        Assert.Equal(15, buffer.GetCommittedLength());

        Assert.False(buffer.TryGetFirstCommittedSpan(true, out _));
        Assert.True(buffer.TryGetFirstCommittedSpan(false, out committed));
        Assert.True(committed.SequenceEqual("partial consume"u8));
        buffer.DiscardCommitted(8);
        Assert.False(buffer.CommittedIsEmpty);
        Assert.Equal(7, buffer.GetCommittedLength());
        Assert.True(buffer.TryGetFirstCommittedSpan(false, out committed));
        Assert.True(committed.SequenceEqual("consume"u8));
        buffer.DiscardCommitted(7);
        Assert.True(buffer.CommittedIsEmpty);
        Assert.Equal(0, buffer.GetCommittedLength());
        Assert.False(buffer.TryGetFirstCommittedSpan(false, out _));
        buffer.Release();
    }

    private sealed class CountingMemoryPool(MemoryPool<byte>? tail = null) : MemoryPool<byte>
    {
        private readonly MemoryPool<byte> _tail = tail ?? MemoryPool<byte>.Shared;
        private int count;

        public int Count => Volatile.Read(ref count);
        public override IMemoryOwner<byte> Rent(int minBufferSize = -1) => new Wrapper(this, _tail.Rent(minBufferSize));

        protected override void Dispose(bool disposing) => throw new NotImplementedException();

        private void Decrement() => Interlocked.Decrement(ref count);

        private CountingMemoryPool Increment()
        {
            Interlocked.Increment(ref count);
            return this;
        }

        public override int MaxBufferSize => _tail.MaxBufferSize;

        private sealed class Wrapper(CountingMemoryPool parent, IMemoryOwner<byte> tail) : IMemoryOwner<byte>
        {
            private int _disposed;
            private readonly CountingMemoryPool _parent = parent.Increment();

            public void Dispose()
            {
                if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0)
                {
                    _parent.Decrement();
                    tail.Dispose();
                }
                else
                {
                    ThrowDisposed();
                }
            }

            private void ThrowDisposed() => throw new ObjectDisposedException(nameof(MemoryPool<byte>));

            public Memory<byte> Memory
            {
                get
                {
                    if (Volatile.Read(ref _disposed) != 0) ThrowDisposed();
                    return tail.Memory;
                }
            }
        }
    }

    [Fact]
    public void SkipAggregate()
    {
        var reader = new RespReader("*1\r\n$3\r\nabc\r\n"u8); // ["abc"]
        reader.MoveNext();
        reader.SkipChildren();
        Assert.False(reader.TryMoveNext());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MultiSegmentUsage(bool multiSegmentRead)
    {
        byte[] garbage = new byte[1024 * 1024];
        var rand = new Random(Seed: 134521);
        rand.NextBytes(garbage);

        int offset = 0;
        var mgr = new CountingMemoryPool();
        CycleBuffer buffer = CycleBuffer.Create(mgr);
        Assert.Equal(0, mgr.Count);
        while (offset < garbage.Length)
        {
            var size = rand.Next(1, garbage.Length - offset + 1);
            Debug.Assert(size > 0);
            buffer.Write(new ReadOnlySpan<byte>(garbage, offset, size));
            offset += size;
            Assert.Equal(offset, buffer.GetCommittedLength());
        }

        Assert.True(mgr.Count >= 50); // some non-trivial count
        int total = 0;
        if (multiSegmentRead)
        {
            while (!buffer.CommittedIsEmpty)
            {
                var seq = buffer.GetAllCommitted();
                var take = rand.Next((int)Math.Min(seq.Length, 4 * buffer.PageSize)) + 1;
                var slice = seq.Slice(0, take);
                Assert.True(SequenceEqual(slice, new(garbage, total, take)), "data integrity check");
                buffer.DiscardCommitted(take);
                total += take;
            }
        }
        else
        {
            while (buffer.TryGetFirstCommittedSpan(true, out var span))
            {
                var take = rand.Next(span.Length) + 1;
                var slice = span.Slice(0, take);
                Assert.True(slice.SequenceEqual(new(garbage, total, take)), "data integrity check");
                buffer.DiscardCommitted(take);
                total += take;
            }
        }

        Assert.Equal(garbage.Length, total);
        Assert.Equal(3, mgr.Count);
        buffer.Release();

        Assert.Equal(0, mgr.Count);

        static bool SequenceEqual(ReadOnlySequence<byte> seq1, ReadOnlySpan<byte> seq2)
        {
            if (seq1.IsSingleSegment)
            {
                return seq1.First.Span.SequenceEqual(seq2);
            }

            if (seq1.Length != seq2.Length) return false;
            var arr = ArrayPool<byte>.Shared.Rent(seq2.Length);
            seq1.CopyTo(arr);
            var result = arr.AsSpan(0,  seq2.Length).SequenceEqual(seq2);
            ArrayPool<byte>.Shared.Return(arr);
            return result;
        }
    }
}
