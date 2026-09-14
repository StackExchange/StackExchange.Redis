using System;
using System.Text;
using RESPite.Messages;
using StackExchange.Redis;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// <see cref="ReadOnlyLease{T}"/>: the lease that may share, because nothing can write through it.
/// </summary>
/// <remarks>
/// The pair expresses one rule - share what cannot be written, copy what can. See design notes 6.16.
/// </remarks>
public class ReadOnlyLeaseTests
{
    private static RespResult Reply(string raw) => RespResult.Capture(Encoding.UTF8.GetBytes(raw));

    [Fact]
    public void ItSharesTheReplyBufferRatherThanCopying()
    {
        // the whole point: one more reference to the reply, not a second copy of it
        using var reply = Reply("$5\r\nhello\r\n");
        Assert.Equal(1, reply.RefCount);

        var reader = reply.ReadScalar();
        using var lease = reader.ReadLease();

        Assert.NotNull(lease);
        Assert.Equal("hello", Encoding.UTF8.GetString(lease!.Span.ToArray()));
        Assert.Equal(2, reply.RefCount);   // shared, not copied
    }

    [Fact]
    public void DisposingTheLeaseGivesTheReferenceBack()
    {
        using var reply = Reply("$5\r\nhello\r\n");
        var reader = reply.ReadScalar();

        var lease = reader.ReadLease();
        Assert.Equal(2, reply.RefCount);

        lease!.Dispose();
        Assert.Equal(1, reply.RefCount);

        // once-only, however many times it is called - the release underneath is not idempotent
        lease.Dispose();
        lease.Dispose();
        Assert.Equal(1, reply.RefCount);
    }

    [Fact]
    public void TheMutableLeaseCopiesInstead()
    {
        // a Lease<byte> is writable - Span, Memory and ArraySegment all - so it must never point at memory
        // anything else can read. It therefore takes no reference from the reply.
        using var reply = Reply("$5\r\nhello\r\n");
        var reader = reply.ReadScalar();

#pragma warning disable CS0618 // retired on purpose; this test is what it was retired FOR
        using var lease = RespReaderExtensions.ReadLease(in reader);
#pragma warning restore CS0618

        Assert.NotNull(lease);
        Assert.Equal("hello", Encoding.UTF8.GetString(lease!.Span.ToArray()));
        Assert.Equal(1, reply.RefCount);   // copied: the reply is untouched
    }

    [Fact]
    public void WritingThroughTheMutableLeaseCannotReachTheReply()
    {
        // the concrete reason for the split: scribble on the mutable lease and the reply is unharmed
        using var reply = Reply("$5\r\nhello\r\n");
        var reader = reply.ReadScalar();

#pragma warning disable CS0618
        using var mutable = RespReaderExtensions.ReadLease(in reader);
#pragma warning restore CS0618
        mutable!.Span.Fill((byte)'X');

        Assert.Equal("hello", reply.ReadScalar().ReadString());
    }

    [Fact]
    public void ToArrayLetsASmallValueOutliveALargeReply()
    {
        // sharing pins: the lease holds the whole reply alive. ToArray is the way out when a small value
        // has to outlive a large one.
        var big = new string('x', 4096);
        byte[] copied;
        using (var reply = Reply($"$1\r\na\r\n"))
        {
            var reader = reply.ReadScalar();
            using var lease = reader.ReadLease();
            copied = lease!.ToArray();
        }

        Assert.Equal("a", Encoding.UTF8.GetString(copied));
        GC.KeepAlive(big);
    }

    [Fact]
    public void ANullScalarIsNull()
    {
        using var reply = Reply("$-1\r\n");
        var reader = reply.Read();
        Assert.Null(reader.ReadLease());
    }

    [Fact]
    public void AnEmptyScalarIsTheSharedEmpty()
    {
        using var reply = Reply("$0\r\n\r\n");
        var reader = reply.ReadScalar();

        var lease = reader.ReadLease();
        Assert.Same(ReadOnlyLease<byte>.Empty, lease);
        Assert.Equal(1, reply.RefCount);   // nothing to share
    }
}
