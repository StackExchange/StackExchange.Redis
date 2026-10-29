using System;
using System.Text;
using System.Threading;
using RESPite.Messages;
using RESPite.Operations;
using Xunit;

namespace RESPite.Tests;

// see the note in RespOperationTests; the analyzers misfire on this type's API shape
#pragma warning disable xUnit1031, xUnit1051

/// <summary>
/// The diagnostics an operation carries - design notes section 4. The highest-risk part of the core
/// replacement, because nothing fails a test when it goes missing: the cost is a timeout exception that
/// says "it timed out" instead of naming the problem.
/// </summary>
public class RespOperationDiagnosticsTests
{
    private sealed class PingMessage : RespMessageBase<string?>
    {
        protected override string? Parse(ref RespReader reader)
            => reader.TryGetSpan(out var span) ? Encoding.UTF8.GetString(span.ToArray()) : null;

        internal PingMessage Attach()
        {
            SetRequest(Encoding.UTF8.GetBytes("*1\r\n$4\r\nPING\r\n"), null, default);
            return this;
        }
    }

    [Fact]
    public void AttachingARequestStartsTheLadder()
    {
        var message = new PingMessage();
        Assert.Equal(RespCommandStatus.Unknown, message.Diagnostics.Status);

        message.Attach();
        Assert.Equal(RespCommandStatus.WaitingToBeSent, message.Diagnostics.Status);
        Assert.NotEqual(default, message.Diagnostics.CreatedDateTime);
        Assert.NotEqual(0, message.Diagnostics.CreatedTimestamp);
    }

    [Fact]
    public void TakingTheBytesAdvancesItToSent()
    {
        var message = new PingMessage().Attach();

        Assert.True(message.TryReserveRequest(message.Token, out _));
        Assert.Equal(RespCommandStatus.Sent, message.Diagnostics.Status);

        message.ReleaseRequest();
    }

    [Fact]
    public void PeekingAtTheBytesDoesNotClaimItWasSent()
    {
        // recordSent:false exists for the paths that look without writing; claiming Sent there would
        // make a command that never reached a socket ineligible for the retry bypass below
        var message = new PingMessage().Attach();

        Assert.True(message.TryReserveRequest(message.Token, out _, recordSent: false));
        Assert.Equal(RespCommandStatus.WaitingToBeSent, message.Diagnostics.Status);

        message.ReleaseRequest();
    }

    [Fact]
    public void NotAppliedTracksTheLadderAndNotTheOutcome()
    {
        // this is the half that is NOT merely diagnostic: RetryPolicy reads it to bypass the side-effect
        // cap, so losing the ladder makes retry silently more conservative
        var message = new PingMessage().Attach();
        Assert.True(message.Diagnostics.IsKnownNotApplied);

        message.Diagnostics.Status = RespCommandStatus.WaitingInBacklog;
        Assert.True(message.Diagnostics.IsKnownNotApplied); // still never handed to a socket

        Assert.True(message.TryReserveRequest(message.Token, out _));
        Assert.False(message.Diagnostics.IsKnownNotApplied); // now it might have been applied
        message.ReleaseRequest();
    }

    [Fact]
    public void EnqueueingRecordsTheConnectionAndItsCounters()
    {
        var message = new PingMessage().Attach();
        var connection = new object();

        message.Diagnostics.OnEnqueued(connection, bytesSent: 1814, bytesReceived: 1718);

        Assert.Same(connection, message.Diagnostics.EnqueuedTo);
        Assert.Equal(1814, message.Diagnostics.QueuedStampSent);
        Assert.Equal(1718, message.Diagnostics.QueuedStampReceived);
        Assert.NotEqual(0, message.Diagnostics.WriteTickCount);
    }

    [Fact]
    public void ASyntheticOperationRendersAReport()
    {
        // the point of phase 2: decide the shape while it is cheap, and prove the fields survive the
        // trip from "something went wrong" to "here is what it was doing"
        var message = new PingMessage().Attach();
        message.Diagnostics.OnEnqueued("connection-7", bytesSent: 1814, bytesReceived: 1718);
        message.Diagnostics.HighIntegrityToken = 0xDEADBEEF;

        var builder = new StringBuilder();
        message.Diagnostics.Describe(builder);
        var report = builder.ToString();

        Assert.Contains("status: WaitingToBeSent", report);
        Assert.Contains("not-applied: True", report);
        Assert.Contains("enqueued-to: connection-7", report);
        Assert.Contains("qs: 1814", report);
        Assert.Contains("qr: 1718", report);
        Assert.Contains("hi-token: 3735928559", report);
        Assert.Contains("age: ", report);
    }

    [Fact]
    public void ASentOperationDoesNotClaimItWasNotApplied()
    {
        var message = new PingMessage().Attach();
        Assert.True(message.TryReserveRequest(message.Token, out _));

        var report = message.Diagnostics.ToString();
        Assert.Contains("status: Sent", report);
        Assert.DoesNotContain("not-applied", report);

        message.ReleaseRequest();
    }

    [Fact]
    public void ARecycledOperationReportsItsOwnLifeAndNotTheLastOne()
    {
        // THE pooling hazard, and the reason the inventory is one struct: a report describing the
        // previous command is worse than no report, because it is believed
        var message = new PingMessage().Attach();
        message.Diagnostics.OnEnqueued("connection-7", bytesSent: 1814, bytesReceived: 1718);
        message.Diagnostics.HighIntegrityToken = 0xDEADBEEF;
        Assert.True(message.TryReserveRequest(message.Token, out _));
        message.ReleaseRequest();

        var first = message.Diagnostics.CreatedTimestamp;
        message.TrySetResult(message.Token, "$4\r\nPONG\r\n"u8);
        Assert.Equal("PONG", message.GetResult(message.Token));

        Thread.Sleep(2); // so the new stamp is distinguishable from the old
        message.Attach();

        Assert.Equal(RespCommandStatus.WaitingToBeSent, message.Diagnostics.Status);
        Assert.Null(message.Diagnostics.EnqueuedTo);
        Assert.Equal(0, message.Diagnostics.QueuedStampSent);
        Assert.Equal(0, message.Diagnostics.QueuedStampReceived);
        Assert.Equal(0u, message.Diagnostics.HighIntegrityToken);
        Assert.NotEqual(first, message.Diagnostics.CreatedTimestamp);

        var report = message.Diagnostics.ToString();
        Assert.DoesNotContain("connection-7", report);
        Assert.DoesNotContain("1814", report);
    }

    [Fact]
    public void AgeIsZeroRatherThanNonsenseBeforeAnythingIsStamped()
    {
        var diagnostics = default(RespOperationDiagnostics);
        Assert.Equal(TimeSpan.Zero, diagnostics.Age);
    }

    [Fact]
    public void TheStatusLadderMatchesTheHostsShippedEnum()
    {
        // mapping is meant to be an identity rather than a switch somebody has to keep correct; if the
        // host's CommandStatus ever changes, this is where it is noticed
        Assert.Equal(0, (int)RespCommandStatus.Unknown);
        Assert.Equal(1, (int)RespCommandStatus.WaitingToBeSent);
        Assert.Equal(2, (int)RespCommandStatus.Sent);
        Assert.Equal(3, (int)RespCommandStatus.WaitingInBacklog);
    }
}
