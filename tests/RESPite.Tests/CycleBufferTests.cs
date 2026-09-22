using System;
using System.Buffers;
using System.Linq;
using System.Threading;
using RESPite.Buffers;
using Xunit;

namespace RESPite.Tests;

public class CycleBufferTests()
{
    [Fact]
    public void WriteMultiSegmentSequence_WritesEverySegment()
    {
        // three segments, each with distinct content and differing lengths; a regression guard against
        // writing the first segment repeatedly instead of walking each segment
        var expected = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 };
        var seq = CreateSequence(new byte[] { 0, 1, 2 }, new byte[] { 3, 4, 5, 6 }, new byte[] { 7, 8 });
        Assert.False(seq.IsSingleSegment);

        var buffer = CycleBuffer.Create();
        buffer.Write(in seq);

        Assert.Equal(expected.Length, buffer.GetCommittedLength());
        Assert.Equal(expected, buffer.GetAllCommitted().ToArray());
    }

    private static ReadOnlySequence<byte> CreateSequence(params byte[][] chunks)
    {
        Segment? head = null, tail = null;
        long runningIndex = 0;
        foreach (var chunk in chunks)
        {
            var next = new Segment(chunk, runningIndex);
            if (tail is null) head = next;
            else tail.SetNext(next);
            tail = next;
            runningIndex += chunk.Length;
        }
        return new ReadOnlySequence<byte>(head!, 0, tail!, tail!.Memory.Length);
    }

    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        public Segment(ReadOnlyMemory<byte> memory, long runningIndex)
        {
            Memory = memory;
            RunningIndex = runningIndex;
        }

        public void SetNext(Segment next) => Next = next;
    }

    public enum Timing
    {
        CommitEverythingBeforeDiscard,
        CommitAfterFirstDiscard,
    }

    [Theory]
    [InlineData(Timing.CommitEverythingBeforeDiscard)]
    [InlineData(Timing.CommitAfterFirstDiscard)]
    public void CanDiscardSafely(Timing timing)
    {
        var buffer = CycleBuffer.Create();
        buffer.GetUncommittedSpan(10).Slice(0, 10).Fill(1);
        Assert.Equal(0, buffer.GetCommittedLength());
        buffer.Commit(10);
        Assert.Equal(10, buffer.GetCommittedLength());
        buffer.GetUncommittedSpan(15).Slice(0, 15).Fill(2);

        if (timing is Timing.CommitEverythingBeforeDiscard) buffer.Commit(15);

        Assert.True(buffer.TryGetFirstCommittedSpan(1, out var committed));
        switch (timing)
        {
            case Timing.CommitEverythingBeforeDiscard:
                Assert.Equal(25, committed.Length);
                for (int i = 0; i < 10; i++)
                {
                    if (1 != committed[i])
                    {
                        Assert.Fail($"committed[{i}]={committed[i]}");
                    }
                }
                for (int i = 10; i < 25; i++)
                {
                    if (2 != committed[i])
                    {
                        Assert.Fail($"committed[{i}]={committed[i]}");
                    }
                }
                break;
            case Timing.CommitAfterFirstDiscard:
                Assert.Equal(10, committed.Length);
                for (int i = 0; i < committed.Length; i++)
                {
                    if (1 != committed[i])
                    {
                        Assert.Fail($"committed[{i}]={committed[i]}");
                    }
                }
                break;
        }

        buffer.DiscardCommitted(committed.Length);
        Assert.Equal(0, buffer.GetCommittedLength());

        // now (simulating concurrent) we commit the second span
        if (timing is Timing.CommitAfterFirstDiscard)
        {
            buffer.Commit(15);

            Assert.Equal(15, buffer.GetCommittedLength());

            // and we should be able to read those bytes
            Assert.True(buffer.TryGetFirstCommittedSpan(1, out committed));
            Assert.Equal(15, committed.Length);
            for (int i = 0; i < committed.Length; i++)
            {
                if (2 != committed[i])
                {
                    Assert.Fail($"committed[{i}]={committed[i]}");
                }
            }

            buffer.DiscardCommitted(committed.Length);
        }

        Assert.Equal(0, buffer.GetCommittedLength());
    }

    /// <summary>
    /// Stress test for the filler/parser split used by <c>PhysicalConnection.ReadAllAsync</c>: one real
    /// thread keeps writing (GetUncommittedMemory+Commit) while another concurrently reads and discards
    /// (TryGetCommitted/GetAllCommitted+DiscardCommitted), synchronized only by an external lock - exactly
    /// the pattern the production code relies on, since CycleBuffer itself has no internal thread-safety.
    /// A running byte pattern makes any loss, duplication, reordering, or corruption immediately visible.
    /// </summary>
    [Fact]
    public void ConcurrentFillAndParse_PreservesDataUnderStress()
    {
        var buffer = CycleBuffer.Create();
        var bufferLock = new object();
        const long TargetBytes = 500_000; // several dozen 8KB segments' worth
        long totalWritten = 0, totalVerified = 0;
        Exception? fillerError = null, parserError = null;

        var filler = new Thread(() =>
        {
            try
            {
                var rng = new Random(12345);
                long written = 0;
                while (written < TargetBytes)
                {
                    Memory<byte> mem;
                    lock (bufferLock)
                    {
                        mem = buffer.GetUncommittedMemory();
                    }

                    var chunkLen = Math.Min(Math.Min(mem.Length, rng.Next(1, 4001)), (int)Math.Min(TargetBytes - written, int.MaxValue));
                    var span = mem.Span[..chunkLen];
                    for (int i = 0; i < chunkLen; i++)
                    {
                        span[i] = unchecked((byte)(written + i));
                    }

                    lock (bufferLock)
                    {
                        buffer.Commit(chunkLen);
                    }
                    written += chunkLen;
                    Volatile.Write(ref totalWritten, written);
                }
            }
            catch (Exception ex)
            {
                fillerError = ex;
            }
        });

        var parser = new Thread(() =>
        {
            try
            {
                long verified = 0;
                while (verified < TargetBytes)
                {
                    bool single;
                    ReadOnlySpan<byte> span = default;
                    ReadOnlySequence<byte> seq = default;
                    lock (bufferLock)
                    {
                        single = buffer.TryGetCommitted(out span);
                        if (!single) seq = buffer.GetAllCommitted();
                    }

                    long consumed;
                    if (single)
                    {
                        consumed = VerifyAndCount(span, verified);
                    }
                    else
                    {
                        consumed = 0;
                        foreach (var segment in seq)
                        {
                            consumed += VerifyAndCount(segment.Span, verified + consumed);
                        }
                    }

                    if (consumed > 0)
                    {
                        lock (bufferLock)
                        {
                            buffer.DiscardCommitted(consumed);
                        }
                        verified += consumed;
                        Volatile.Write(ref totalVerified, verified);
                    }
                    else
                    {
                        Thread.Sleep(0); // nothing new yet; yield rather than hot-spin
                    }
                }
            }
            catch (Exception ex)
            {
                parserError = ex;
            }

            static long VerifyAndCount(ReadOnlySpan<byte> span, long expectedStart)
            {
                for (int i = 0; i < span.Length; i++)
                {
                    var expectedByte = unchecked((byte)(expectedStart + i));
                    if (span[i] != expectedByte)
                    {
                        throw new InvalidOperationException(
                            $"Data mismatch at offset {expectedStart + i}: expected {expectedByte}, got {span[i]}");
                    }
                }
                return span.Length;
            }
        });

        filler.Start();
        parser.Start();
        var fillerDone = filler.Join(TimeSpan.FromSeconds(10));
        var parserDone = parser.Join(TimeSpan.FromSeconds(10));
        if (!fillerDone || !parserDone)
        {
            long committedNow;
            lock (bufferLock)
            {
                committedNow = buffer.GetCommittedLength();
            }
            Assert.Fail(
                $"stalled: fillerDone={fillerDone} parserDone={parserDone} totalWritten={Volatile.Read(ref totalWritten)} " +
                $"totalVerified={Volatile.Read(ref totalVerified)} committedLength={committedNow} " +
                $"fillerError={fillerError} parserError={parserError}");
        }

        Assert.Null(fillerError);
        Assert.Null(parserError);
        Assert.Equal(TargetBytes, totalWritten);
        Assert.Equal(TargetBytes, totalVerified);
    }
}
