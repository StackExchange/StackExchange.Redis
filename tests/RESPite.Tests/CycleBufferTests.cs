using System;
using System.Buffers;
using System.Linq;
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
    /// A segment recycled while start-trimmed must not carry its trim into the next buffer that is handed it.
    /// </summary>
    /// <remarks>
    /// Recycled segments go to a process-wide spare slot and come back out of <c>Segment.Create</c> as pristine.
    /// <c>Recycle</c> did not reset <c>StartTrimCount</c>, and some paths - <see cref="CycleBuffer.Release"/>, and
    /// <c>AppendOrRecycle</c>'s search-exhausted path - recycle without <c>Untrim</c> first, so the next buffer's
    /// "new" segment wrote at a stale offset and read its committed bytes from the wrong place. Found under #3251's
    /// read/parse stress test as <c>Debug.Assert(leasedStart == 0)</c>; absorbed into v4 with the split.
    /// </remarks>
    [Fact]
    public void ARecycledTrimmedSegmentStartsCleanInTheNextBuffer()
    {
        // the spare slot is process-wide and the suite is parallel, so another buffer can take the segment first;
        // repeat the scenario rather than assert on a race nobody controls
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var first = CycleBuffer.Create();
            first.Write("0123456789"u8);
            first.DiscardCommitted(4);   // start-trimmed: StartTrimCount is now 4
            first.Release();             // recycled WITHOUT Untrim

            var second = CycleBuffer.Create();

            second.Write("hello"u8);

            // read directly, because in Release - where the Debug.Assert is compiled out - nothing visible fails
            // at once: the stale trim only corrupts the chain arithmetic of a later Untrim
            Assert.Equal(0, StartTrimCountOfFirstSegment(second));
            Assert.True(second.TryGetCommitted(out var committed));
            Assert.Equal("hello", System.Text.Encoding.ASCII.GetString(committed.ToArray()));
            second.Release();
        }
    }

    private static int StartTrimCountOfFirstSegment(CycleBuffer buffer)
    {
        const System.Reflection.BindingFlags Any = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        var segment = typeof(CycleBuffer).GetField("startSegment", Any)!.GetValue(buffer)!;
        return (int)segment.GetType().GetProperty("StartTrimCount", Any)!.GetValue(segment)!;
    }
}
