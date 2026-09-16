using System;
using System.Collections.Generic;
using System.Text;
using RESPite.Messages;
using Xunit;

namespace RESPite.Tests;

/// <summary>
/// <see cref="RespAggregate{T}"/>: a window over an aggregate frame, projecting children on demand.
/// </summary>
public class RespAggregateTests
{
    private static byte[] Frame(string s) => Encoding.UTF8.GetBytes(s.Replace("|", "\r\n"));

    private static readonly RespReader.Projection<object?, string> ReadString =
        static (ref object? owner, ref RespReader r) => r.ReadString() ?? "";

    private static RespAggregate<T> Capture<T>(byte[] frame, RespReader.Projection<object?, T> projection)
    {
        var reader = new RespReader(frame);
        Assert.True(RespAggregate<T>.TryCaptureNext(frame, ref reader, projection, out var value));
        return value;
    }

    [Fact]
    public void WalksAFlatAggregate()
    {
        var agg = Capture(Frame("*3|$1|a|$1|b|$1|c|"), ReadString);

        Assert.Equal(3, agg.Count);

        var seen = new List<string>();
        foreach (var item in agg) seen.Add(item);
        Assert.Equal(["a", "b", "c"], seen);
    }

    /// <summary>Walking twice re-reads the buffer: a window holds no parse state of its own.</summary>
    [Fact]
    public void CanBeWalkedMoreThanOnce()
    {
        var agg = Capture(Frame("*2|$1|a|$1|b|"), ReadString);

        var first = new List<string>();
        foreach (var item in agg) first.Add(item);
        var second = new List<string>();
        foreach (var item in agg) second.Add(item);

        Assert.Equal(first, second);
    }

    /// <summary>
    /// The nested case, which is the one that motivates the type: an XRANGE-shaped reply where each child
    /// is itself an aggregate, captured as a window over the same buffer.
    /// </summary>
    [Fact]
    public void WalksNestedAggregatesOverOneBuffer()
    {
        var frame = Frame("*2|*2|$3|1-1|*2|$1|f|$1|v|*2|$3|1-2|*2|$1|g|$1|w|");

        var entries = Capture<string>(frame, static (ref object? owner, ref RespReader r) =>
        {
            // the entry: [id, [name, value, ...]]
            var children = r.AggregateChildren();
            children.DemandNext();
            var id = children.Value.ReadString();
            children.DemandNext();
            return id + "/" + children.Value.AggregateLength();
        });

        Assert.Equal(2, entries.Count);

        var seen = new List<string>();
        foreach (var entry in entries) seen.Add(entry);
        Assert.Equal(["1-1/2", "1-2/2"], seen);
    }

    /// <summary>A nil aggregate reads as empty, as every other aggregate shape on this surface does.</summary>
    [Fact]
    public void ANilAggregateIsEmpty()
    {
        var agg = Capture(Frame("*-1|"), ReadString);

        Assert.Equal(0, agg.Count);
        foreach (var item in agg) Assert.Fail("a nil aggregate yielded " + item);
    }

    [Fact]
    public void AnEmptyAggregateIsEmpty()
    {
        var agg = Capture(Frame("*0|"), ReadString);

        Assert.Equal(0, agg.Count);
        foreach (var item in agg) Assert.Fail("an empty aggregate yielded " + item);
    }

    /// <summary>default(RespAggregate) enumerates as nothing rather than throwing.</summary>
    [Fact]
    public void TheDefaultIsAnEmptyAggregate()
    {
        var agg = default(RespAggregate<string>);

        Assert.Equal(0, agg.Count);
        foreach (var item in agg) Assert.Fail("the default yielded " + item);
    }

    /// <summary>The capture brackets the sub-tree exactly, leaving the outer reader on the next sibling.</summary>
    [Fact]
    public void CaptureLeavesTheReaderOnTheNextSibling()
    {
        var frame = Frame("*2|*2|$1|a|$1|b|$4|tail|");
        var reader = new RespReader(frame);
        reader.MoveNext(); // the outer run

        Assert.True(RespAggregate<string>.TryCaptureNext(frame, ref reader, ReadString, out var inner));
        Assert.Equal(2, inner.Count);

        // the outer reader skipped the whole sub-tree, so the next element is the sibling scalar
        reader.MoveNext();
        Assert.Equal("tail", reader.ReadString());
    }

    [Fact]
    public void ToArrayMaterialises()
    {
        var agg = Capture(Frame("*3|$1|a|$1|b|$1|c|"), ReadString);

        Assert.Equal(["a", "b", "c"], agg.ToArray());
        Assert.Empty(default(RespAggregate<string>).ToArray());
        Assert.Empty(Capture(Frame("*0|"), ReadString).ToArray());
    }

    /// <summary>
    /// A truncated frame is caught by the <b>reader</b>, during capture - not by a count check afterwards.
    /// </summary>
    /// <remarks>
    /// Worth pinning, because it is why <c>ToArray</c> has no short-fill guard: RESP framing makes "fewer
    /// children than the header promised" unreachable, since the count is how many the reader reads. A
    /// guard there would be dead code that reads as load-bearing.
    /// </remarks>
    [Fact]
    public void ATruncatedFrameThrowsOutOfTheWalk()
    {
        static void Capture()
        {
            var bytes = Frame("*3|$1|a|$1|b|");   // *3 declared, two supplied
            var reader = new RespReader(bytes);
            RespAggregate<string>.TryCaptureNext(bytes, ref reader, ReadString, out _);
        }

        Assert.ThrowsAny<Exception>(Capture);
    }

    /// <summary>
    /// <see cref="RespAggregate{T}.ToString"/> touches no bytes, so the owner going away cannot affect it.
    /// </summary>
    [Fact]
    public void ToStringReadsNoBytes()
    {
        var owner = new Releasable(Frame("*2|$1|a|$1|b|"));
        var reader = new RespReader(owner.Bytes);
        Assert.True(RespAggregate<string>.TryCaptureNext(owner, ref reader, ReadString, out var agg));

        Assert.Equal("(2 items)", agg.ToString());

        // Count came off the header at capture, so this still answers after the buffer has gone back
        owner.Release();
        Assert.Equal("(2 items)", agg.ToString());
        Assert.Throws<ObjectDisposedException>(() => agg.ToArray());   // reading the children does not
    }

    private sealed class Releasable(byte[] bytes) : IRespBufferOwner
    {
        private bool _released;

        public byte[] Bytes => bytes;

        public void Release() => _released = true;

        public ReadOnlySpan<byte> GetReadOnlySpan()
            => _released ? throw new ObjectDisposedException(nameof(Releasable)) : bytes;
    }

    /// <summary>A scalar is not an aggregate, and says so rather than reading as an empty one.</summary>
    [Fact]
    public void AScalarIsRejected()
    {
        var frame = Frame("$3|abc|");

        // a ref local cannot be captured, so the attempt goes in a local function
        static void Capture(byte[] bytes)
        {
            var reader = new RespReader(bytes);
            RespAggregate<string>.TryCaptureNext(bytes, ref reader, ReadString, out _);
        }

        Assert.ThrowsAny<InvalidOperationException>(() => Capture(frame));
    }
}
