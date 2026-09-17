using System;
using System.Text;
using RESPite.Messages;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// PROTOTYPE, not a proposal in code: what a deferred walk over the reply buffer would cost.
/// </summary>
/// <remarks>
/// <para>
/// Lives in the test project on purpose - it needs nothing that is not already public
/// (<see cref="RespReader"/>, <see cref="IRespBufferOwner"/>), so the shape can be measured before any of
/// it is committed to a shipped surface.
/// </para>
/// <para>
/// The claim under test: the gnarly nested shapes, and possibly the flat ones too, do not need a
/// materialised array at all. The reply is one contiguous buffer; an element is a window over it; a nested
/// element is a window over the same buffer. Walking on demand costs no storage beyond the reply.
/// </para>
/// </remarks>
public class RespAggregateProtoTests(ITestOutputHelper log)
{
    private const int Elements = 1000;

    /// <summary>A window over an aggregate frame, projecting its children on demand.</summary>
    private readonly struct RespAggregate<T>(object? owner, int start, int length, RespReader.Projection<T> projection)
    {
        private ReadOnlySpan<byte> Frame => owner switch
        {
            null => default,
            IRespBufferOwner o => o.GetReadOnlySpan().Slice(start, length),
            _ => new ReadOnlySpan<byte>((byte[])owner, start, length),
        };

        /// <summary>Captures the element the reader is about to read, whatever its shape.</summary>
        /// <remarks>
        /// The whole primitive, and it needs nothing new: BytesConsumed either side of
        /// MoveNext + SkipChildren is exactly the sub-tree's window, which is the same guarantee
        /// RespValue's capture already rests on - just without DemandScalar.
        /// </remarks>
        public static RespAggregate<T> Capture(object? owner, scoped ref RespReader reader, RespReader.Projection<T> projection)
        {
            var before = (int)reader.BytesConsumed;
            reader.MoveNext();
            reader.SkipChildren();
            return new((int)reader.BytesConsumed is var after ? owner : owner, before, after - before, projection);
        }

        /// <summary>
        /// Enumeration is the only access, deliberately: there is no indexer and there will not be one.
        /// </summary>
        /// <remarks>
        /// An indexer would be O(i) - RESP is forward-only with variable-length frames and no offset table -
        /// so a plain <c>for</c> loop over one would be O(n^2) while looking exactly like a list. The shape
        /// of the API is the promise, so the shape has to be one that can be kept.
        /// </remarks>
        public Enumerator GetEnumerator() => new(Frame, projection);

        public ref struct Enumerator
        {
            private RespReader.AggregateEnumerator _children;
            private readonly RespReader.Projection<T> _projection;

            internal Enumerator(ReadOnlySpan<byte> frame, RespReader.Projection<T> projection)
            {
                var reader = new RespReader(frame);
                reader.MoveNext(); // onto the aggregate header
                _children = reader.AggregateChildren();
                _projection = projection;
                Current = default!;
            }

            public T Current { get; private set; }

            public bool MoveNext()
            {
                if (!_children.MoveNext()) return false;
                Current = _projection(ref _children.Value);
                return true;
            }
        }
    }

    private static byte[] Reply()
    {
        var payload = new string('x', 32);
        var sb = new StringBuilder().Append('*').Append(Elements).Append("\r\n");
        for (var i = 0; i < Elements; i++)
        {
            sb.Append('$').Append(payload.Length).Append("\r\n").Append(payload).Append("\r\n");
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static long Measure(Action action)
    {
        action();
        var before = GC.GetAllocatedBytesForCurrentThread();
        action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static readonly RespReader.Projection<int> LengthOf = static (ref r) => r.ScalarLength();
    private static readonly RespReader.Projection<int> One = static (ref r) => 1;

    [Fact]
    public void ADeferredWalkNeedsNoStorageAtAll()
    {
        var reply = Reply();

        var payloadOnly = Measure(() =>
        {
            using var payload = RespPayload.Create(reply);
            Assert.NotNull(payload);
        });

        var asLease = Measure(() =>
        {
            using var payload = RespPayload.Create(reply);
            using var lease = ((IRespPayloadHandler<ReadOnlyLease<RespValue>>)RespHandlers.ValueWindowHandler.Lease).Parse(payload);
            var total = 0;
            for (var i = 0; i < lease.Length; i++) total += lease.Span[i].Length;
            Assert.Equal(Elements * 32, total);
        });

        var captureOnly = Measure(() =>
        {
            using var payload = RespPayload.Create(reply);
            var reader = payload.GetReader();
            var agg = RespAggregate<int>.Capture(payload, ref reader, LengthOf);
            Assert.True(agg.GetEnumerator().MoveNext());
        });

        var bareReader = Measure(() =>
        {
            var reader = new RespReader(reply);
            reader.MoveNext();
            var n = 0;
            var iter = reader.AggregateChildren();
            while (iter.MoveNext()) n++;
            Assert.Equal(Elements, n);
        });

        var trivialWalk = Measure(() =>
        {
            using var payload = RespPayload.Create(reply);
            var reader = payload.GetReader();
            var agg = RespAggregate<int>.Capture(payload, ref reader, One);
            var n = 0;
            foreach (var one in agg) n += one;
            Assert.Equal(Elements, n);
        });

        var asWalk = Measure(() =>
        {
            using var payload = RespPayload.Create(reply);
            var reader = payload.GetReader();
            var agg = RespAggregate<int>.Capture(payload, ref reader, LengthOf);
            var total = 0;
            foreach (var length in agg) total += length;
            Assert.Equal(Elements * 32, total);
        });

        log.WriteLine(
            $"{Elements} values: payload alone {payloadOnly:n0}B, capture {captureOnly:n0}B, " +
            $"bare child walk {bareReader:n0}B, walk with trivial projection {trivialWalk:n0}B, " +
            $"lease-of-windows {asLease:n0}B, deferred walk {asWalk:n0}B");

        // The robust facts, and deliberately not a comparison of the two totals - they came out 736 vs 696,
        // which is far too close to assert on and is the finding rather than the win:
        //   * capturing a window costs NOTHING beyond the payload that already exists;
        //   * the projection is not the cost - a trivial one measures the same as a real one;
        //   * so the ~600B both paths share is the WALK, which the lease path pays too while filling its
        //     array. The pooled array is already nearly free; the lease object is the ~40B between them.
        Assert.Equal(payloadOnly, captureOnly);
        Assert.Equal(trivialWalk, asWalk);
    }

    /// <summary>The nested case, which is where materialising costs N+1 arrays.</summary>
    [Fact]
    public void ANestedWalkReadsTheRightThings()
    {
        var frame = Encoding.UTF8.GetBytes("*2|*2|$3|1-1|*2|$1|f|$1|v|*2|$3|1-2|*2|$1|g|$1|w|".Replace("|", "\r\n"));
        var reader = new RespReader(frame);

        // each child is itself an aggregate, captured as a window over the same buffer
        var entries = RespAggregate<string>.Capture(frame, ref reader, static (ref r) =>
        {
            // the entry: [id, [name, value, ...]] - read the id, and count the inner run
            var iter = r.AggregateChildren();
            iter.DemandNext();
            var id = iter.Value.ReadString();
            iter.DemandNext();
            return id + "/" + iter.Value.AggregateLength();
        });

        var seen = new System.Collections.Generic.List<string>();
        foreach (var entry in entries) seen.Add(entry);

        Assert.Equal(["1-1/2", "1-2/2"], seen);
    }
}
