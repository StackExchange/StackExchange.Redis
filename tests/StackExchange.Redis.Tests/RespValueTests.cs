using System;
using System.Text;
using RESPite.Messages;
using StackExchange.Redis.Interpolated;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// <c>RespValue</c>: one scalar reply value as a window over somebody else's buffer.
/// </summary>
public class RespValueTests
{
    private static byte[] Frame(string resp) => Encoding.UTF8.GetBytes(resp.Replace("|", "\r\n"));

    /// <summary>Capture every element of an array reply, all sharing the one buffer.</summary>
    private static RespValue[] CaptureAll(byte[] frame)
    {
        var reader = new RespReader(frame);
        reader.MoveNext();
        var count = reader.AggregateLength();

        var values = new RespValue[count];
        for (var i = 0; i < count; i++)
        {
            Assert.True(RespValue.TryCaptureNext(frame, ref reader, out values[i]));
        }

        return values;
    }

    [Fact]
    public void TheReaderBracketsEachElement()
    {
        // THE assumption the capture rests on: BytesConsumed counts to the END of what was just read, so
        // sampling it either side of a move gives that element's window and nothing else has to be added
        // to the reader. If this ever stops being true, every RespValue silently points at the wrong bytes.
        var frame = Frame("*2|$3|abc|:123|");
        var reader = new RespReader(frame);
        reader.MoveNext();

        Assert.Equal(4, reader.BytesConsumed); // past "*2\r\n"

        reader.MoveNext();
        Assert.Equal(13, reader.BytesConsumed); // past "$3\r\nabc\r\n"

        reader.MoveNext();
        Assert.Equal(19, reader.BytesConsumed); // past ":123\r\n"
    }

    [Fact]
    public void AValueIsAWindowOntoTheFrameItCameFrom()
    {
        var frame = Frame("*2|$3|abc|:123|");
        var values = CaptureAll(frame);

        // the whole point: two values, one buffer, no copies
        Assert.Equal("$3\r\nabc\r\n", Encoding.UTF8.GetString(values[0].Frame.ToArray()));
        Assert.Equal(":123\r\n", Encoding.UTF8.GetString(values[1].Frame.ToArray()));
    }

    [Fact]
    public void TheAccessorsAreTheReadersAccessors()
    {
        var values = CaptureAll(Frame("*4|$3|abc|:123|$5|12.25|#t|"));

        Assert.Equal("abc", values[0].AsString());
        Assert.Equal(123, values[1].AsInt32());
        Assert.Equal(123L, values[1].AsInt64());
        Assert.Equal(12.25, values[2].AsDouble());
        Assert.True(values[3].AsBoolean());

        // a bulk string of digits and an integer are both integers, because the reader says so - there is
        // no second coercion table here to disagree with it
        Assert.Equal(123, values[1].AsInt32());
    }

    [Fact]
    public void NilIsAbsentRatherThanEmpty()
    {
        var values = CaptureAll(Frame("*2|$-1|$0||"));

        Assert.True(values[0].IsNull);
        Assert.False(values[0].TryGetSpan(out _));
        Assert.Null(values[0].AsString());

        // and the empty string is present-and-empty, which is a different answer. The reader says "true,
        // empty span" for BOTH, so the nil check has to happen here or the two collapse into one.
        Assert.False(values[1].IsNull);
        Assert.True(values[1].TryGetSpan(out var empty));
        Assert.True(empty.IsEmpty);
        Assert.Equal("", values[1].AsString());
    }

    [Fact]
    public void EqualityNormalisesTheSpelling()
    {
        var values = CaptureAll(Frame("*3|:1|$1|1|$1|2|"));

        // :1 and $1\r\n1 are the same value spelled two ways, and RedisValue has always said so; comparing
        // frame bytes would say otherwise, quietly, in every dictionary anyone builds out of these
        Assert.Equal(values[0], values[1]);
        Assert.Equal(values[0].GetHashCode(), values[1].GetHashCode());
        Assert.NotEqual(values[0], values[2]);
    }

    [Fact]
    public void CopyingOutIsTheWayToOutliveTheBuffer()
    {
        var values = CaptureAll(Frame("*1|$3|abc|"));

        Assert.Equal("abc", (string?)values[0].ToRedisValue());

        Span<byte> target = stackalloc byte[values[0].Length];
        Assert.Equal(3, values[0].CopyTo(target));
        Assert.Equal("abc", Encoding.UTF8.GetString(target.ToArray()));
    }

    [Fact]
    public void ReadingThroughADisposedLeaseThrows()
    {
        var lease = ReadOnlyLease<byte>.Rent(16, null, out var target);
        Frame("$3|abc|").CopyTo(target);

        var reader = new RespReader(lease.Span.Slice(0, 9));
        Assert.True(RespValue.TryCaptureNext(lease, ref reader, out var value));
        Assert.Equal("abc", value.AsString());

        // the reason the value holds the OWNER rather than a ReadOnlyMemory: the lease nulls its buffer on
        // the way back to the pool, so a stale read says so instead of returning whatever landed there next
        lease.Dispose();
        Assert.Throws<ObjectDisposedException>(() => value.AsString());
    }

    [Fact]
    public void AnAggregateIsNotAValue()
    {
        var frame = Frame("*1|*1|:1|");

        // scalar by construction, enforced where it is captured rather than tested in every accessor -
        // an aggregate element is a caller bug, not a server one
        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            var reader = new RespReader(frame);
            reader.MoveNext();
            RespValue.TryCaptureNext(frame, ref reader, out _);
        });

        Assert.Contains("requires a scalar element", ex.Message);
    }
}
