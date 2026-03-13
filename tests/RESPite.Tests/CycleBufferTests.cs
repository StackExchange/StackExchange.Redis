using System;
using RESPite.Buffers;
using Xunit;

namespace RESPite.Tests;

public class CycleBufferTests()
{
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
}
