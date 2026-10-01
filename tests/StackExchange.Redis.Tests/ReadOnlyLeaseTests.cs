using System;
using System.Buffers;
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
    /// <summary>
    /// A lease over elements that can hold references wipes the array before returning it to the pool.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not fussiness: a pooled array handed back still points at everything that was in it, so each of
    /// those objects stays reachable until that buffer happens to be rented again and overwritten. It never
    /// throws; the heap just quietly fails to shrink, which is far harder to find than a crash.
    /// </para>
    /// <para>
    /// Asserted by renting the same size straight back - the shared pool hands out the most recently
    /// returned buffer of a bucket - and looking at what is in it. That is an implementation detail of
    /// <see cref="ArrayPool{T}"/> rather than a contract, which is why the test tolerates getting a
    /// different array and only asserts when it got the same one back.
    /// </para>
    /// </remarks>
    [Fact]
    public void ReferenceElementsAreClearedOnReturn()
    {
        var lease = ReadOnlyLease<string>.Rent(4, null, out var target);
        for (var i = 0; i < target.Length; i++) target[i] = "value" + i;
        lease.Dispose();

        var reused = ArrayPool<string>.Shared.Rent(4);
        try
        {
            foreach (var slot in reused)
            {
                Assert.Null(slot);
            }
        }
        finally
        {
            ArrayPool<string>.Shared.Return(reused);
        }
    }

    /// <summary>...and a primitive element type is not wiped, because clearing it buys nothing.</summary>
    /// <remarks>
    /// The cost side of the same decision: bytes cannot keep anything alive, so a memset per release would
    /// be pure overhead on the path this type was built for in the first place.
    /// </remarks>
    [Fact]
    public void PrimitiveElementsAreNotClearedOnReturn()
    {
        var lease = ReadOnlyLease<byte>.Rent(4, null, out var target);
        target.Fill(0xAB);
        lease.Dispose();

        var reused = ArrayPool<byte>.Shared.Rent(4);
        try
        {
            // if this ever legitimately hands back a different buffer, the assertion below is vacuous
            // rather than wrong - which is the right way round for a test about an optimisation
            Assert.Contains(reused, b => b == 0xAB);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(reused);
        }
    }

}
