using System;
using System.Text;
using RESPite.Messages;
using StackExchange.Redis.Interpolated;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// The two aggregate forms of an element type must read a reply the same way.
/// </summary>
/// <remarks>
/// Every element type here is read by an array handler and a lease handler, and until the projections were
/// hoisted onto <c>RespHandlers.Elements</c> each pair was two hand-written lambdas up to 250 lines apart.
/// They agreed, but nothing made them, and a pair that disagreed would be invisible: both calls succeed and
/// return the length the caller expected. Sharing the projection makes divergence unexpressible for these;
/// this catches the next type added that writes its own instead.
/// </remarks>
public class RespAggregateFormTests
{
    // the defaults object implements every interface, so one instance casts to whichever form is wanted
    private static readonly IRespHandler<long> Defaults = RespHandlers.Int64;

    private static RespReader ReaderOver(byte[] reply)
    {
        var reader = new RespReader(reply);
        reader.MoveNext();
        return reader;
    }

    private static void AssertFormsAgree<T>(string reply)
    {
        var bytes = Encoding.UTF8.GetBytes(reply);

        var forArray = ReaderOver(bytes);
        var asArray = ((IRespHandler<T[]>)Defaults).Parse(ref forArray);

        var forLease = ReaderOver(bytes);
        using var asLease = ((IRespHandler<ReadOnlyLease<T>>)Defaults).Parse(ref forLease);

        Assert.NotEmpty(asArray); // never let an empty reply pass this vacuously
        Assert.Equal(asArray.Length, asLease.Length);
        for (var i = 0; i < asArray.Length; i++)
        {
            Assert.Equal(asArray[i], asLease.Span[i]);
        }
    }

    [Fact]
    public void Int64FormsAgree() => AssertFormsAgree<long>("*3\r\n:1\r\n:-2\r\n:9223372036854775807\r\n");

    [Fact]
    public void BooleanFormsAgree() => AssertFormsAgree<bool>("*3\r\n:1\r\n:0\r\n:1\r\n");

    [Fact]
    public void ExpireResultFormsAgree() => AssertFormsAgree<ExpireResult>("*3\r\n:1\r\n:0\r\n:-2\r\n");

    [Fact]
    public void PersistResultFormsAgree() => AssertFormsAgree<PersistResult>("*2\r\n:1\r\n:-1\r\n");

    /// <remarks>ZMSCORE replies nil for a member that is not there, so the nil element is the point.</remarks>
    [Fact]
    public void NullableDoubleFormsAgree() => AssertFormsAgree<double?>("*3\r\n$3\r\n1.5\r\n$-1\r\n$2\r\n-2\r\n");

    /// <summary>
    /// The pair types agree too, in <b>both</b> wire shapes: RESP2 interleaves a row, RESP3 may nest it.
    /// </summary>
    /// <remarks>
    /// Which shape arrives is decided from the reply's content rather than the negotiated protocol, so both
    /// are reachable on either connection - and the array and lease forms must agree on both. They now
    /// derive from one processor per row type (<c>ReadPairArray</c>/<c>ReadPairLease</c>), which is what
    /// makes that structural rather than a coincidence worth re-checking.
    /// </remarks>
    [Theory]
    [InlineData("*4\r\n$1\r\na\r\n$1\r\n1\r\n$1\r\nb\r\n$1\r\n2\r\n")]                     // RESP2: interleaved
    [InlineData("*2\r\n*2\r\n$1\r\na\r\n$1\r\n1\r\n*2\r\n$1\r\nb\r\n$1\r\n2\r\n")]     // RESP3: jagged
    public void HashEntryFormsAgree(string reply) => AssertFormsAgree<HashEntry>(reply);

    /// <inheritdoc cref="HashEntryFormsAgree"/>
    [Theory]
    [InlineData("*4\r\n$1\r\na\r\n$1\r\n1\r\n$1\r\nb\r\n$1\r\n2\r\n")]
    [InlineData("*2\r\n*2\r\n$1\r\na\r\n$1\r\n1\r\n*2\r\n$1\r\nb\r\n$1\r\n2\r\n")]
    public void SortedSetEntryFormsAgree(string reply) => AssertFormsAgree<SortedSetEntry>(reply);

    /// <summary>
    /// The one element type with <b>three</b> forms, and so the one most likely to drift: the writable
    /// lease walks the reply itself rather than going through <c>ReadScalarLease</c>.
    /// </summary>
    [Fact]
    public void NullableInt64AgreesAcrossBothLeaseForms()
    {
        var bytes = Encoding.UTF8.GetBytes("*3\r\n:1\r\n$-1\r\n:-2\r\n");

        var forWritable = ReaderOver(bytes);
        using var writable = ((IRespHandler<Lease<long?>>)Defaults).Parse(ref forWritable);

        var forReadOnly = ReaderOver(bytes);
        using var readOnly = ((IRespHandler<ReadOnlyLease<long?>>)Defaults).Parse(ref forReadOnly);

        Assert.Equal(3, writable.Length);
        Assert.Equal(writable.Length, readOnly.Length);
        for (var i = 0; i < writable.Length; i++)
        {
            Assert.Equal(writable.Span[i], readOnly.Span[i]);
        }

        // and the nil really is a nil, not a zero that happens to match on both sides
        Assert.Null(readOnly.Span[1]);
    }
}
