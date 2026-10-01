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

    // a projection is handed a reader positioned BEFORE its child - the same position TryCaptureNext
    // expects - so every projection here steps in first
    private static readonly RespReader.Projection<object?, string> ReadString =
        static (ref object? owner, ref RespReader r) =>
        {
            r.MoveNext();
            return r.ReadString() ?? "";
        };

    /// <summary>Captures each child as a window, rather than reading it.</summary>
    private static readonly RespReader.Projection<object?, RespValue> CaptureValue =
        static (ref object? owner, ref RespReader r) =>
        {
            RespValue.TryCaptureNext(owner, ref r, out var value);
            return value;
        };

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
            r.MoveNext();
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

    /// <summary>
    /// A window captured <i>inside</i> a walk must point at the owner's bytes, not at the slice the walk
    /// happened to be reading.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the test that pins the position base.</b> A walk reads a slice of the owner's buffer,
    /// but a window is recorded as an offset into the whole buffer - so unless the reader is told where
    /// its slice starts, every offset captured during the walk is short by exactly that amount. The
    /// failure is silent: the window is well-formed and resolves to <i>some</i> bytes, just the wrong
    /// ones, and it only bites below the top level, where the enclosing start is no longer zero.
    /// </para>
    /// <para>
    /// So the second entry's fields are what matter here: the first entry starts at zero and reads
    /// correctly either way.
    /// </para>
    /// </remarks>
    [Fact]
    public void AWindowCapturedInsideANestedWalkPointsAtTheOwnersBytes()
    {
        var frame = Frame("*2|*2|$3|1-1|*2|$1|f|$1|v|*2|$3|1-2|*2|$1|g|$1|w|");

        // each entry becomes a window over its own field list, captured during the outer walk
        var entries = Capture<RespAggregate<RespValue>>(frame, static (ref object? owner, ref RespReader r) =>
        {
            r.MoveNext();                                    // onto the entry
            RespValue.TryCaptureNext(owner, ref r, out _);   // past the id
            RespAggregate<RespValue>.TryCaptureNext(owner, ref r, CaptureValue, out var fields);
            return fields;
        });

        var lists = entries.ToArray();
        Assert.Equal(2, lists.Length);

        // walking a captured field list, and capturing from inside THAT walk: two levels of slice
        Assert.Equal(["f", "v"], Read(lists[0]));
        Assert.Equal(["g", "w"], Read(lists[1]));

        static List<string?> Read(RespAggregate<RespValue> fields)
        {
            var seen = new List<string?>();
            foreach (var field in fields) seen.Add((string?)field);
            return seen;
        }
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

    /// <summary>
    /// The prefix is what distinguishes the aggregate kinds, since <c>Count</c> cannot.
    /// </summary>
    /// <remarks>
    /// A map of two pairs is <c>%2</c> and reports <b>4</b> children, which is indistinguishable from an
    /// array of four without this.
    /// </remarks>
    [Theory]
    [InlineData("*2|$1|a|$1|b|", RespPrefix.Array, 2)]
    [InlineData("%2|$1|a|$1|b|$1|c|$1|d|", RespPrefix.Map, 4)]
    [InlineData("~2|$1|a|$1|b|", RespPrefix.Set, 2)]
    [InlineData(">2|$1|a|$1|b|", RespPrefix.Push, 2)]
    public void ThePrefixSaysWhichKindOfAggregate(string resp, RespPrefix expected, int count)
    {
        var agg = Capture(Frame(resp), ReadString);

        Assert.Equal(expected, agg.Prefix);
        Assert.Equal(count, agg.Count);
    }

    /// <summary>
    /// The same logical reply counts and walks the same in RESP2 and RESP3, even though the wire differs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>HGETALL</c> arrives as a flat <c>*4</c> on RESP2 and as a <c>%2</c> map on RESP3. The reader
    /// already normalises that: a map reports its <b>element</b> count, so both say 4 and both walk 4. The
    /// protocol shows up in <see cref="RespAggregate{T}.Prefix"/> and nowhere else.
    /// </para>
    /// <para>
    /// Worth pinning because the tempting "fix" runs the other way: dividing a map's count by two to report
    /// <i>pairs</i> would make RESP3 say 2 where RESP2 says 4, inventing the very difference this removes -
    /// and would break the invariant that matters more, since <c>Count</c> is what the enumerator yields.
    /// Pairs belong to a pairwise projection, where <c>T</c> is the pair and both shapes give 2.
    /// </para>
    /// </remarks>
    [Fact]
    public void Resp2AndResp3AgreeOnCountAndWalk()
    {
        var resp2 = Capture(Frame("*4|$1|a|$1|1|$1|b|$1|2|"), ReadString);   // flat array
        var resp3 = Capture(Frame("%2|$1|a|$1|1|$1|b|$1|2|"), ReadString);   // map

        Assert.Equal(RespPrefix.Array, resp2.Prefix);
        Assert.Equal(RespPrefix.Map, resp3.Prefix);   // the only thing that differs

        Assert.Equal(resp2.Count, resp3.Count);
        Assert.Equal(4, resp3.Count);
        Assert.Equal(resp2.ToArray(), resp3.ToArray());
        Assert.Equal(["a", "1", "b", "2"], resp3.ToArray());
    }

    [Fact]
    public void AnAggregateWithNoBytesHasNoPrefix()
        => Assert.Equal(RespPrefix.None, default(RespAggregate<string>).Prefix);

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

    [Fact]
    public void RespValueCopiesToChars()
    {
        // the value half of the text API: same decode, reached through a captured window rather than a
        // live reader, which is the shape a caller actually holds after a reply is parsed
        var frame = Frame("*2|$5|hello|$6|a€z!|");
        var values = Capture(frame, CaptureValue).ToArray();

        Span<char> target = stackalloc char[16];
        Assert.Equal(5, values[0].CopyTo(target));
        Assert.Equal("hello", target.Slice(0, 5).ToString());

        var chars = values[1].CopyTo(target);
        Assert.Equal("a€z!", target.Slice(0, chars).ToString());

        // and the byte twin still says the same thing, in its own units
        Span<byte> bytes = stackalloc byte[16];
        Assert.Equal(6, values[1].CopyTo(bytes)); // "a" + 3 bytes of euro + "z" + "!"
    }

    [Fact]
    public void RespValueCopiesToCharsWithAnExplicitEncoding()
    {
        // bytes that are NOT UTF-8; reading them as UTF-8 would give replacement characters, which is the
        // case the encoding parameter exists for
        var frame = new byte[] { (byte)'$', (byte)'3', 13, 10, 0xE9, 0xE8, 0xFC, 13, 10 };
        var reader = new RespReader(frame);
        Assert.True(RespValue.TryCaptureNext(frame, ref reader, out var value));

        Span<char> target = stackalloc char[8];
        Assert.Equal(3, value.CopyTo(target, Encoding.GetEncoding(28591)));
        Assert.Equal("éèü", target.Slice(0, 3).ToString());
    }
}
