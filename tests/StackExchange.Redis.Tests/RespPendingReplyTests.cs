using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// The <c>XPENDING</c> reply shapes, read as windows over their own buffer.
/// </summary>
/// <remarks>
/// The same two claims as <see cref="RespRangeReplyTests"/>: the deferred view and the materialised shape
/// agree because they are two call sites of one parse, and the windows die with the reply. The summary is
/// the more interesting of the two - its consumer count arrives as a <b>bulk string</b> rather than an
/// integer, which is the sort of thing a hand-written walk gets wrong and a shared parse cannot.
/// </remarks>
public class RespPendingReplyTests
{
    private sealed class FakeExecutor(string reply) : RespExecutorBase
    {
        public override int Database => 0;

        public override RespPayload Send(in RespRequest request) => RespPayload.Create(Encoding.UTF8.GetBytes(reply));

        public override ValueTask<RespPayload> SendAsync(RespRequest request, CancellationToken cancellationToken = default)
            => new(Send(request));
    }

    private static RespDatabaseContext Context(string reply)
        => new(new RespContext().WithExecutor(new FakeExecutor(reply)));

    private const string Summary =
        "*4\r\n:2\r\n$3\r\n1-1\r\n$3\r\n9-9\r\n*2\r\n*2\r\n$3\r\nbob\r\n$1\r\n2\r\n*2\r\n$3\r\njoe\r\n$1\r\n8\r\n";

    private const string NoConsumers = "*4\r\n:0\r\n_\r\n_\r\n_\r\n";

    private const string Messages =
        "*2\r\n*4\r\n$3\r\n1-1\r\n$3\r\nbob\r\n:1234\r\n:3\r\n*4\r\n$3\r\n2-2\r\n$3\r\njoe\r\n:5\r\n:1\r\n";

    [Fact]
    public async Task TheSummaryWalksItsConsumers()
    {
        using var reply = await Context(Summary).Streams.PendingAsync("s", "g");

        Assert.Equal(2, reply.PendingMessageCount);
        Assert.Equal("1-1", reply.LowestPendingMessageId.ToString());
        Assert.Equal("9-9", reply.HighestPendingMessageId.ToString());

        var consumers = reply.Consumers.ToArray();
        Assert.Equal(2, consumers.Length);
        Assert.Equal("bob", consumers[0].Name.ToString());
        Assert.Equal(2, consumers[0].PendingMessageCount);
        Assert.Equal("joe", consumers[1].Name.ToString());
        Assert.Equal(8, consumers[1].PendingMessageCount);
    }

    /// <summary>The deferred walk and the materialised struct say the same thing.</summary>
    [Fact]
    public async Task TheSummaryAgreesWithTheMaterialisedShape()
    {
        using var reply = await Context(Summary).Streams.PendingAsync("s", "g");
        var info = reply.ToStreamPendingInfo();

        Assert.Equal(reply.PendingMessageCount, info.PendingMessageCount);
        Assert.Equal(reply.LowestPendingMessageId.ToString(), info.LowestPendingMessageId.ToString());
        Assert.Equal(reply.HighestPendingMessageId.ToString(), info.HighestPendingMessageId.ToString());
        Assert.Equal(
            reply.Consumers.ToArray().Select(c => $"{c.Name}:{c.PendingMessageCount}"),
            info.Consumers.Select(c => $"{c.Name}:{c.PendingMessageCount}"));
    }

    /// <summary>
    /// An empty group replies with nulls in the last three slots, which must not become an exception.
    /// </summary>
    /// <remarks>
    /// The shipped processor has a comment about exactly this case, so it is a shape the server really
    /// sends rather than a defensive guess.
    /// </remarks>
    [Fact]
    public async Task AnEmptyGroupIsNotAnError()
    {
        using var reply = await Context(NoConsumers).Streams.PendingAsync("s", "g");

        Assert.Equal(0, reply.PendingMessageCount);
        Assert.Empty(reply.Consumers.ToArray());
        Assert.Empty(reply.ToStreamPendingInfo().Consumers);
    }

    [Fact]
    public async Task TheMessagesWalkAndMaterialiseTheSame()
    {
        using var reply = await Context(Messages).Streams.PendingMessagesAsync("s", "g", 10);

        Assert.Equal(2, reply.Count);
        var walked = reply.Messages.ToArray();
        var array = reply.ToArray();

        Assert.Equal(
            walked.Select(m => $"{m.MessageId}|{m.ConsumerName}|{(long)m.IdleTime.TotalMilliseconds}|{m.DeliveryCount}"),
            array.Select(m => $"{m.MessageId}|{m.ConsumerName}|{m.IdleTimeInMilliseconds}|{m.DeliveryCount}"));

        Assert.Equal("1-1", array[0].MessageId);
        Assert.Equal(TimeSpan.FromMilliseconds(1234), walked[0].IdleTime);
        Assert.Equal(3, walked[0].DeliveryCount);
    }

    /// <summary>One record, materialised on its own, matches the array's.</summary>
    [Fact]
    public async Task ARecordCanOutliveTheReply()
    {
        StreamPendingMessageInfo copied;
        using (var reply = await Context(Messages).Streams.PendingMessagesAsync("s", "g", 10))
        {
            copied = reply.Messages.ToArray()[0].ToStreamPendingMessageInfo();
        }

        // the buffer is back in the pool by here; the copy must not point into it
        Assert.Equal("1-1", copied.MessageId);
        Assert.Equal("bob", copied.ConsumerName);
        Assert.Equal(1234, copied.IdleTimeInMilliseconds);
        Assert.Equal(3, copied.DeliveryCount);
    }

    private const string AutoClaim =
        "*3\r\n$3\r\n0-0\r\n*1\r\n*2\r\n$3\r\n1-1\r\n*2\r\n$1\r\nf\r\n$1\r\nv\r\n*1\r\n$3\r\n7-7\r\n";

    /// <summary>A 6.2 server sends two elements, not three.</summary>
    private const string AutoClaimNoDeleted =
        "*2\r\n$3\r\n0-0\r\n*1\r\n*2\r\n$3\r\n1-1\r\n*2\r\n$1\r\nf\r\n$1\r\nv\r\n";

    private const string AutoClaimIds = "*3\r\n$3\r\n0-0\r\n*2\r\n$3\r\n1-1\r\n$3\r\n2-2\r\n*1\r\n$3\r\n7-7\r\n";

    [Fact]
    public async Task AutoClaimWalksAndMaterialisesTheSame()
    {
        using var reply = await Context(AutoClaim).Streams.AutoClaimAsync("s", "g", "c", TimeSpan.Zero, "0-0");
        var materialised = reply.ToStreamAutoClaimResult();

        Assert.Equal("0-0", reply.NextStartId.ToString());
        Assert.Equal(reply.NextStartId.ToString(), materialised.NextStartId.ToString());
        Assert.Equal(
            reply.ClaimedEntries.ToArray().Select(e => e.Id.ToString()),
            materialised.ClaimedEntries.Select(e => e.Id.ToString()));
        Assert.Equal(
            reply.DeletedIds.ToArray().Select(v => v.ToString()),
            materialised.DeletedIds.Select(v => v.ToString()));
        Assert.Equal("7-7", Assert.Single(materialised.DeletedIds));
    }

    /// <summary>
    /// The pre-7.0 two-element reply reads as "no deleted ids" rather than as an error.
    /// </summary>
    [Fact]
    public async Task AutoClaimToleratesAMissingDeletedList()
    {
        using var reply = await Context(AutoClaimNoDeleted).Streams.AutoClaimAsync("s", "g", "c", TimeSpan.Zero, "0-0");

        Assert.Equal("0-0", reply.NextStartId.ToString());
        Assert.Single(reply.ClaimedEntries.ToArray());
        Assert.Empty(reply.DeletedIds.ToArray());
        Assert.Empty(reply.ToStreamAutoClaimResult().DeletedIds);
    }

    [Fact]
    public async Task AutoClaimIdsOnlyWalksAndMaterialisesTheSame()
    {
        using var reply = await Context(AutoClaimIds).Streams.AutoClaimIdsOnlyAsync("s", "g", "c", TimeSpan.Zero, "0-0");
        var materialised = reply.ToStreamAutoClaimIdsOnlyResult();

        Assert.Equal(
            reply.ClaimedIds.ToArray().Select(v => v.ToString()),
            materialised.ClaimedIds.Select(v => v.ToString()));
        Assert.Equal(["1-1", "2-2"], materialised.ClaimedIds.Select(v => v.ToString()));
        Assert.Equal("7-7", Assert.Single(materialised.DeletedIds).ToString());
    }

    /// <summary>And the windows really are dead once the reply is.</summary>
    [Fact]
    public async Task ReadingAfterDisposalThrows()
    {
        var reply = await Context(Summary).Streams.PendingAsync("s", "g");
        reply.Dispose();

        Assert.Throws<ObjectDisposedException>(() => reply.Consumers);
        Assert.Throws<ObjectDisposedException>(() => reply.LowestPendingMessageId);
    }
}
