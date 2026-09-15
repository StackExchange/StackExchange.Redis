using System;
using System.Buffers;
using System.Text;
using RESPite.Messages;
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

        Assert.Equal("abc", (string?)values[0]);
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
        Assert.Null((string?)values[0]);

        // and the empty string is present-and-empty, which is a different answer. The reader says "true,
        // empty span" for BOTH, so the nil check has to happen here or the two collapse into one.
        Assert.False(values[1].IsNull);
        Assert.True(values[1].TryGetSpan(out var empty));
        Assert.True(empty.IsEmpty);
        Assert.Equal("", (string?)values[1]);
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

        Assert.Equal("abc", (string?)values[0].AsRedisValue());

        Span<byte> target = stackalloc byte[values[0].Length];
        Assert.Equal(3, values[0].CopyTo(target));
        Assert.Equal("abc", Encoding.UTF8.GetString(target.ToArray()));
    }

    /// <summary>Capture one value out of a lease-owned buffer.</summary>
    private static (ReadOnlyLease<byte> Lease, RespValue Value) InLease(string resp, MemoryPool<byte>? pool = null)
    {
        var bytes = Frame(resp);
        var lease = ReadOnlyLease<byte>.Rent(bytes.Length, pool, out var target);
        bytes.CopyTo(target);

        var reader = new RespReader(lease.Span.Slice(0, bytes.Length));
        Assert.True(RespValue.TryCaptureNext(lease, ref reader, out var value));
        return (lease, value);
    }

    /// <summary>
    /// Every way of getting at the bytes, each with a frame it can actually read.
    /// </summary>
    /// <remarks>
    /// The frames differ because the reader's coercions are stricter than <see cref="RedisValue"/>'s: a
    /// bulk <c>"1"</c> is not a boolean to it, because a server never answers a boolean that way.
    /// </remarks>
    public static TheoryData<string, string, Action<RespValue>> Accessors => new()
    {
        { "Frame", "$1|1|", v => _ = v.Frame.Length },
        { "(string?)", "$1|1|", v => _ = (string?)v },
        { "AsInt64", "$1|1|", v => v.AsInt64() },
        { "AsInt32", "$1|1|", v => v.AsInt32() },
        { "AsDouble", "$1|1|", v => v.AsDouble() },
        { "AsBoolean", ":1|", v => v.AsBoolean() },
        { "AsRedisValue", "$1|1|", v => v.AsRedisValue() },
        { "Length", "$1|1|", v => _ = v.Length },
        { "TryGetSpan", "$1|1|", v => v.TryGetSpan(out _) },
        { "CopyTo", "$1|1|", v => { Span<byte> target = stackalloc byte[8]; v.CopyTo(target); } },
        { "Equals", "$1|1|", v => v.Equals(v) },
        { "GetHashCode", "$1|1|", v => v.GetHashCode() },
        { "ToString", "$1|1|", v => v.ToString() },
    };

    [Theory]
    [MemberData(nameof(Accessors))]
    public void EveryAccessorDiesWithTheLease(string name, string resp, Action<RespValue> accessor)
    {
        var (lease, value) = InLease(resp);
        accessor(value); // works while the lease is alive

        // the reason the value holds the OWNER rather than a ReadOnlyMemory: the lease nulls its buffer on
        // the way back to the pool, so a stale read says so instead of returning whatever landed there
        // next. One accessor proving that is not the claim - every route to the bytes has to be shut.
        lease.Dispose();

        var ex = Record.Exception(() => accessor(value));
        Assert.True(ex is ObjectDisposedException, $"{name} gave {ex?.GetType().Name ?? "no error"} after disposal");
    }

    [Fact]
    public void APooledLeaseDiesTheSameWay()
    {
        // the other branch of the lease: an IMemoryOwner rather than a pooled array. Dispose nulls the
        // same field, so both land on the same guard - but only one of them was being exercised.
        var (lease, value) = InLease("$3|abc|", MemoryPool<byte>.Shared);
        Assert.Equal("abc", (string?)value);

        lease.Dispose();
        Assert.Throws<ObjectDisposedException>(() => (string?)value);
    }

    [Fact]
    public void AValueOverABareArrayHasNothingToDieWith()
    {
        // the boundary of the guarantee, stated rather than assumed: hardening comes from the OWNER, so a
        // value over a plain array keeps that array alive and stays readable forever. That is correct -
        // nothing recycles it - but it means "it dies with the lease" is a claim about leases, and a
        // handler that hands out a bare array gets no protection from it.
        var frame = Frame("$3|abc|");
        var reader = new RespReader(frame);
        Assert.True(RespValue.TryCaptureNext(frame, ref reader, out var value));

        GC.Collect();
        Assert.Equal("abc", (string?)value);
    }

    /// <summary>Counts how many times it was released, and complains if that is more than once.</summary>
    private sealed class ReleaseProbe : IDisposable
    {
        public int Releases { get; private set; }

        public void Dispose() => Releases++;
    }

    [Fact]
    public void ALeaseReleasesItsSecondaryExactlyOnce()
    {
        var probe = new ReleaseProbe();
        var lease = ReadOnlyLease<byte>.Rent(8, null, out _, probe);

        Assert.Equal(0, probe.Releases);

        lease.Dispose();
        Assert.Equal(1, probe.Releases);

        // the same exchange-to-null that guards the buffer guards this: a second release would decrement
        // somebody else's reference, which is a bug that surfaces nowhere near here
        lease.Dispose();
        lease.Dispose();
        Assert.Equal(1, probe.Releases);
    }

    [Fact]
    public void ALeaseWithoutASecondaryIsUnchanged()
    {
        var lease = ReadOnlyLease<byte>.Rent(4, null, out var target);
        target[0] = 42;

        Assert.Equal(4, lease.Length);
        Assert.Equal(42, lease.Span[0]);

        lease.Dispose();
        Assert.Throws<ObjectDisposedException>(() => lease.Span.Length);
    }

    [Fact]
    public void AnEmptyLeaseStillGivesBackWhatItWasHanded()
    {
        // Rent(0) hands back the shared Empty singleton, which cannot carry anything - so the reference
        // has to go back immediately rather than being dropped on the floor. Without this a zero-length
        // reply would leak its buffer, and zero-length replies are not rare.
        var probe = new ReleaseProbe();
        var lease = ReadOnlyLease<byte>.Rent(0, null, out _, probe);

        Assert.Same(ReadOnlyLease<byte>.Empty, lease);
        Assert.Equal(1, probe.Releases);
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
