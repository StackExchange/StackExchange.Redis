using System;
using System.Collections.Generic;
using System.Text;
using RESPite.Messages;
using Xunit;

namespace RESPite.Tests;

/// <summary>
/// <see cref="RespPairAggregate{T}"/>: a window over a pair-shaped aggregate, projecting pairs on demand.
/// </summary>
/// <remarks>
/// The thing worth pinning throughout is that <b>the same pairs come back from both wire shapes</b>.
/// Interleaved and jagged are how two servers - or the same server on two protocols - spell the identical
/// reply, so a caller must not be able to tell which arrived except by asking.
/// </remarks>
public class RespPairAggregateTests
{
    private static byte[] Frame(string s) => Encoding.UTF8.GetBytes(s.Replace("|", "\r\n"));

    /// <summary>Reads both halves; the plain case, where nothing is captured.</summary>
    private static readonly RespReader.PairProjection<object?, string> ReadPair =
        static (ref object? owner, ref RespReader first, ref RespReader second) =>
        {
            first.MoveNext();
            second.MoveNext();
            return first.ReadString() + "=" + second.ReadString();
        };

    /// <summary>Captures both halves as windows, which is what the deferred surface actually does.</summary>
    private static readonly RespReader.PairProjection<object?, (RespValue Name, RespValue Value)> CapturePair =
        static (ref object? owner, ref RespReader first, ref RespReader second) =>
        {
            RespValue.TryCaptureNext(owner, ref first, out var name);
            RespValue.TryCaptureNext(owner, ref second, out var value);
            return (name, value);
        };

    private static RespPairAggregate<T> Capture<T>(byte[] frame, RespReader.PairProjection<object?, T> projection, bool allowJagged = true)
    {
        var reader = new RespReader(frame);
        Assert.True(RespPairAggregate<T>.TryCaptureNext(frame, ref reader, projection, allowJagged, out var value));
        return value;
    }

    private const string Interleaved = "*4|$1|a|$1|1|$1|b|$1|2|";
    private const string Jagged = "*2|*2|$1|a|$1|1|*2|$1|b|$1|2|";

    [Theory]
    [InlineData(Interleaved, false)]
    [InlineData(Jagged, true)]
    public void BothWireShapesReadAsTheSamePairs(string resp, bool jagged)
    {
        var pairs = Capture(Frame(resp), ReadPair);

        Assert.Equal(2, pairs.Count);
        Assert.Equal(jagged, pairs.IsJagged);

        var seen = new List<string>();
        foreach (var pair in pairs) seen.Add(pair);
        Assert.Equal(["a=1", "b=2"], seen);
    }

    /// <summary>
    /// Jaggedness is read from the <b>content</b>, and permitting it is the caller's call.
    /// </summary>
    /// <remarks>
    /// Read as interleaved, a jagged reply's "pairs" are (inner-array, inner-array) - so refusing jagged
    /// does not merely lose a nicety, it changes what the reply means. That is why the flag exists rather
    /// than the type always guessing.
    /// </remarks>
    [Fact]
    public void JaggednessCanBeRefused()
    {
        var pairs = Capture(Frame(Jagged), ReadPair, allowJagged: false);

        Assert.False(pairs.IsJagged);
        Assert.Equal(1, pairs.Count); // two children, read as one interleaved pair
    }

    /// <summary>An all-scalar reply is never jagged, whatever the caller permits.</summary>
    [Fact]
    public void InterleavedIsNotMistakenForJagged()
    {
        Assert.False(Capture(Frame(Interleaved), ReadPair, allowJagged: true).IsJagged);
    }

    /// <summary>
    /// A run where only <i>some</i> children are two-element aggregates is not jagged.
    /// </summary>
    /// <remarks>
    /// All-or-nothing, because a partially-jagged reply is not a shape any server produces; reading one
    /// half each way would invent a pairing that is not on the wire.
    /// </remarks>
    [Fact]
    public void PartiallyJaggedIsNotJagged()
    {
        Assert.False(Capture(Frame("*2|*2|$1|a|$1|1|$1|b|"), ReadPair).IsJagged);
    }

    /// <summary>An odd trailing element is not half a pair: the walk stops rather than inventing one.</summary>
    [Fact]
    public void AnOddTrailingElementIsNotAPair()
    {
        var pairs = Capture(Frame("*3|$1|a|$1|1|$6|orphan|"), ReadPair);

        Assert.Equal(1, pairs.Count);
        Assert.Equal(["a=1"], pairs.ToArray());
    }

    /// <summary>Windows captured during the walk outlive it, and point at the right bytes.</summary>
    [Theory]
    [InlineData(Interleaved)]
    [InlineData(Jagged)]
    public void CapturedHalvesSurviveTheWalk(string resp)
    {
        var frame = Frame(resp);
        var captured = Capture(frame, CapturePair).ToArray();

        Assert.Equal(2, captured.Length);
        Assert.Equal("a", (string?)captured[0].Name);
        Assert.Equal("1", (string?)captured[0].Value);
        Assert.Equal("b", (string?)captured[1].Name);
        Assert.Equal("2", (string?)captured[1].Value);
    }

    [Fact]
    public void ANilAggregateIsEmpty()
    {
        var pairs = Capture(Frame("*-1|"), ReadPair);

        Assert.Equal(0, pairs.Count);
        Assert.Empty(pairs.ToArray());
        Assert.Equal(RespPrefix.None, pairs.Prefix);
    }

    [Fact]
    public void AnEmptyAggregateIsEmpty()
    {
        var pairs = Capture(Frame("*0|"), ReadPair);

        Assert.Equal(0, pairs.Count);
        Assert.False(pairs.IsJagged); // nothing to be jagged about
        Assert.Empty(pairs.ToArray());
    }

    [Fact]
    public void TheDefaultIsAnEmptyAggregate()
    {
        var pairs = default(RespPairAggregate<string>);

        Assert.Equal(0, pairs.Count);
        Assert.Empty(pairs.ToArray());
        Assert.Equal("(nil)", pairs.ToString());
    }

    /// <summary>A map is the server saying "pairs" outright, rather than this type deciding it.</summary>
    [Fact]
    public void AMapReportsItsPrefixAndItsPairs()
    {
        var pairs = Capture(Frame("%2|$1|a|$1|1|$1|b|$1|2|"), ReadPair);

        Assert.Equal(RespPrefix.Map, pairs.Prefix);
        Assert.Equal(2, pairs.Count);
        Assert.Equal(["a=1", "b=2"], pairs.ToArray());
    }

    /// <summary>
    /// A map and the array that spells the same thing agree on the pairs, which is the RESP2/RESP3 promise.
    /// </summary>
    [Fact]
    public void Resp2AndResp3AgreeOnPairs()
    {
        Assert.Equal(
            Capture(Frame(Interleaved), ReadPair).ToArray(),
            Capture(Frame("%2|$1|a|$1|1|$1|b|$1|2|"), ReadPair).ToArray());
    }

    [Fact]
    public void ToStringCountsPairs()
    {
        Assert.Equal("(2 pairs)", Capture(Frame(Interleaved), ReadPair).ToString());
        Assert.Equal("(1 pair)", Capture(Frame("*2|$1|a|$1|1|"), ReadPair).ToString());
    }

    [Fact]
    public void AScalarIsRejected()
    {
        var frame = Frame("$1|a|");
        var reader = new RespReader(frame);
        Assert.ThrowsAny<Exception>(() =>
        {
            var r = new RespReader(frame);
            RespPairAggregate<string>.TryCaptureNext(frame, ref r, ReadPair, true, out _);
        });
    }

    [Fact]
    public void CaptureLeavesTheReaderOnTheNextSibling()
    {
        var frame = Frame("*2|*2|$1|a|$1|1|$4|tail|");
        var reader = new RespReader(frame);
        reader.MoveNext(); // the outer run

        Assert.True(RespPairAggregate<string>.TryCaptureNext(frame, ref reader, ReadPair, true, out var inner));
        Assert.Equal(1, inner.Count);

        reader.MoveNext();
        Assert.Equal("tail", reader.ReadString());
    }
}
