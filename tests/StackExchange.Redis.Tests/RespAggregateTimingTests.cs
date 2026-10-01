using System;
using System.Diagnostics;
using System.Text;
using RESPite.Messages;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// PROTOTYPE timing: a deferred walk against materialise-then-read, on the shape that motivates it.
/// </summary>
/// <remarks>
/// <para>
/// The allocation experiment (<see cref="RespAggregateProtoTests"/>) came out at 40 bytes per 1000
/// elements on a flat reply, which is thin - but the Cap'n Proto argument this design comes from is about
/// <b>time</b>, and about <b>nested</b> shapes, where materialising costs N+1 arrays and a walk costs none.
/// This measures that.
/// </para>
/// <para>
/// <b>Access is forward-only</b>, once, via <c>foreach</c> - which is what callers actually do, and is
/// also the only pattern where a deferred walk can win: RESP has no offset table, so anything revisiting
/// or indexing pays the walk again.
/// </para>
/// <para>
/// A stopwatch loop, not BenchmarkDotNet: this is sizing a decision, so an order of magnitude is the
/// answer being looked for and a benchmark-grade number would be false precision. Treat a result inside
/// ~2x as "no difference measured".
/// </para>
/// </remarks>
public class RespAggregateTimingTests(ITestOutputHelper log)
{
    private const int Entries = 200, FieldsPerEntry = 5, Iterations = 2000;

    /// <summary>An XRANGE reply: a run of [id, interleaved name/value run].</summary>
    private static byte[] Reply()
    {
        var sb = new StringBuilder().Append('*').Append(Entries).Append("\r\n");
        for (var e = 0; e < Entries; e++)
        {
            var id = $"1789506073259-{e}";
            sb.Append("*2\r\n").Append('$').Append(id.Length).Append("\r\n").Append(id).Append("\r\n");
            sb.Append('*').Append(FieldsPerEntry * 2).Append("\r\n");
            for (var f = 0; f < FieldsPerEntry; f++)
            {
                sb.Append("$6\r\nfield").Append(f).Append("\r\n$8\r\nvalue").Append(f).Append("xx\r\n");
            }
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static (double Micros, long Bytes) Time(Func<int> work, int expected)
    {
        for (var i = 0; i < 200; i++) Assert.Equal(expected, work()); // warm, and prove the work is equal

        var before = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < Iterations; i++) work();
        watch.Stop();
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;

        return ((watch.Elapsed.TotalMilliseconds * 1000) / Iterations, bytes / Iterations);
    }

    [Fact]
    public void ADeferredWalkAgainstMaterialiseThenRead()
    {
        var reply = Reply();
        var expected = Entries * FieldsPerEntry * 8; // every value is 8 bytes

        // ARM A: what happens today - parse the whole reply into StreamEntry[], each holding
        // NameValueEntry[], then read it. N+1 arrays before the caller sees anything.
        var materialise = Time(
            () =>
            {
                var reader = new RespReader(reply);
                reader.MoveNext();
                var entries = ResultProcessor.ParseRedisStreamEntries(ref reader, RedisProtocol.Resp2);
                var total = 0;
                foreach (var entry in entries)
                {
                    foreach (var nv in entry.Values) total += (int)nv.Value.Length();
                }

                return total;
            },
            expected);

        // ARM B: the deferred walk - nothing materialised, each entry read straight off the buffer
        var walk = Time(
            () =>
            {
                var reader = new RespReader(reply);
                reader.MoveNext();
                var total = 0;
                var entries = reader.AggregateChildren();
                while (entries.MoveNext())
                {
                    var entry = entries.Value.AggregateChildren();
                    entry.DemandNext();  // id
                    entry.DemandNext();  // the name/value run
                    var fields = entry.Value.AggregateChildren();
                    while (fields.MoveNext())
                    {
                        fields.MoveNext(); // onto the value
                        total += fields.Value.ScalarLength();
                    }
                }

                return total;
            },
            expected);

        // the earlier prototype attributed ~600B to "the walk"; this arm allocates nothing, so that was
        // wrong. Isolate it: a bare child walk over the FLAT shape, no payload wrapper, no delegate.
        var flat = new StringBuilder().Append("*1000\r\n");
        for (var i = 0; i < 1000; i++) flat.Append("$32\r\n").Append(new string('x', 32)).Append("\r\n");
        var flatReply = Encoding.UTF8.GetBytes(flat.ToString());
        var bare = Time(
            () =>
            {
                var reader = new RespReader(flatReply);
                reader.MoveNext();
                var n = 0;
                var iter = reader.AggregateChildren();
                while (iter.MoveNext()) n += iter.Value.ScalarLength();
                return n;
            },
            1000 * 32);

        log.WriteLine($"bare flat child walk: {bare.Micros:n1}us / {bare.Bytes:n0}B");

        log.WriteLine(
            $"{Entries} entries x {FieldsPerEntry} fields, forward foreach:" +
            $"  materialise {materialise.Micros:n1}us / {materialise.Bytes:n0}B" +
            $"  walk {walk.Micros:n1}us / {walk.Bytes:n0}B");

        // Two assertions, both chosen to survive a faster machine. The timings are logged rather than
        // asserted: ~2x is right at the edge of what a stopwatch loop should be allowed to claim.
        //   * the walk allocates NOTHING, where materialising allocates tens of KB - this is the unambiguous
        //     result, and it is not sensitive to the measurement method;
        //   * the reader walk is allocation-free even on the flat shape, which is why the prototype's
        //     earlier ~600B was its own scaffolding rather than the enumeration.
        Assert.Equal(0, walk.Bytes);
        Assert.Equal(0, bare.Bytes);
        Assert.True(materialise.Bytes > 10_000, $"expected materialising to cost real memory; got {materialise.Bytes:n0}B");
    }
}
