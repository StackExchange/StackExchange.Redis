using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using StackExchange.Redis.Protocol;

namespace StackExchange.Redis.Benchmarks;

/// <summary>
/// What the async machinery costs, with the socket removed.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a fake executor rather than a server.</b> An empty <c>XRANGE</c> round trip is tens of
/// microseconds; the difference this is looking for measured at tens of <i>nanoseconds</i>. Against a real
/// server it is roughly 0.05% of the number and completely buried, so the only way to see it is to take
/// the I/O out. That makes these figures a measure of the machinery and <b>not</b> an end-to-end claim -
/// they must not be compared with server-based numbers.
/// </para>
/// <para>
/// <b>Why <c>XRANGE</c> and not a count.</b> A count is one await through the surface; the transitional
/// shim is the shape that matters - <c>async Task&lt;StreamEntry[]&gt;</c> wrapping a <c>using</c>, an
/// <c>await</c> and a projection - and only a command answering an aggregate exercises it. An
/// <b>empty</b> result keeps that machinery while zeroing the payload, because <c>ToArray()</c> returns
/// <c>Array.Empty&lt;StreamEntry&gt;()</c>.
/// </para>
/// <para>
/// <b>Why both sizes.</b> Empty alone flatters the old shape: with no entries, the array paths allocate
/// nothing while the deferred path still allocates its reply object. That is real, and it is the floor -
/// the point of the deferred shape only appears at size, where the array paths pay N+1 arrays and the
/// walk pays nothing. Reporting only the floor would be choosing the half that agrees with the old API.
/// </para>
/// <para>
/// <b>Why both completion modes.</b> Every fake executor in the test suite completes synchronously, which
/// is the path a cache hit and a buffered pipelined reply take - and the path runtime-async helps most.
/// A fake that only does that would publish the flattering half, so this one also forces a suspension.
/// </para>
/// </remarks>
[Config(typeof(CustomConfig))]
[MemoryDiagnoser]
public class StreamRangeMachineryBenchmarks
{
    /// <summary>
    /// Hands back one pre-built reply, so the harness itself allocates nothing per call.
    /// </summary>
    /// <remarks>
    /// <c>RespPayload.Create</c> copies and rents, which would land in the allocation column and swamp
    /// what is being measured. Retaining a single payload instead balances against the release the
    /// pipeline already does, and contributes zero.
    /// </remarks>
    private sealed class PrebuiltExecutor(RespPayload payload, bool suspend) : IRespExecutor
    {
        public int Database => 0;

        public RespPayload Send(in RespRequest request)
        {
            if (!payload.TryRetain()) throw new ObjectDisposedException(nameof(RespPayload));
            return payload;
        }

        public ValueTask<RespPayload> SendAsync(RespRequest request, CancellationToken cancellationToken = default)
            => suspend ? Suspended() : new(Send(request));

        private async ValueTask<RespPayload> Suspended()
        {
            await default(SuspendInline);
            if (!payload.TryRetain()) throw new ObjectDisposedException(nameof(RespPayload));
            return payload;
        }
    }

    /// <summary>
    /// Suspends once, then resumes <b>inline</b> - so the state-machine box is forced to exist without a
    /// thread hop.
    /// </summary>
    /// <remarks>
    /// <b>The first version of this used <c>Task.Yield()</c>, and the numbers were wrong in a way that
    /// looked like noise and was not.</b> Yielding moves the continuation to a thread-pool thread, so
    /// everything after the await ran in a different threading and GC context - which made the suspending
    /// walk measure <i>faster</i> than the inline one, by 8%, reproducibly, at about twenty standard
    /// errors on a full job. That is not "the same work plus a suspension"; it is different work. An
    /// awaiter that resumes inline measures the box and nothing else.
    /// </remarks>
    private readonly struct SuspendInline : ICriticalNotifyCompletion
    {
        public SuspendInline GetAwaiter() => this;

        public bool IsCompleted => false;

        public void OnCompleted(Action continuation) => continuation();

        public void UnsafeOnCompleted(Action continuation) => continuation();

        public void GetResult()
        {
        }
    }

    private RespPayload _payload = null!;
    private RespDatabaseContext _context;

    /// <summary>Entries in the reply; zero is the machinery floor, 1000 is where the shapes diverge.</summary>
    [Params(0, 1000)]
    public int Entries { get; set; }

    /// <summary>Whether the reply completes inline (a cache hit) or suspends (a real round trip).</summary>
    [Params(false, true)]
    public bool Suspend { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _payload = RespPayload.Create(Encoding.UTF8.GetBytes(BuildReply(Entries)));
        _context = new RespDatabaseContext(new RespContext().WithExecutor(new PrebuiltExecutor(_payload, Suspend)));
    }

    [GlobalCleanup]
    public void Cleanup() => _payload.Dispose();

    private static string BuildReply(int entries)
    {
        var sb = new StringBuilder().Append('*').Append(entries).Append("\r\n");
        for (var e = 0; e < entries; e++)
        {
            sb.Append("*2\r\n$3\r\n").Append((e % 1000).ToString("000")).Append("\r\n*20\r\n");
            for (var f = 0; f < 10; f++) sb.Append("$4\r\nname\r\n$5\r\nvalue\r\n");
        }
        return sb.ToString();
    }

    /// <summary>The deferred shape: hold the reply, walk nothing, give it back.</summary>
    [Benchmark(Baseline = true)]
    public async Task<int> Deferred()
    {
        using var reply = await _context.Streams.RangeAsync("s").ConfigureAwait(false);
        return reply.Count;
    }

    /// <summary>The deferred shape, actually walked - what a caller does with it.</summary>
    [Benchmark]
    public async Task<int> DeferredWalk()
    {
        using var reply = await _context.Streams.RangeAsync("s").ConfigureAwait(false);
        var fields = 0;
        foreach (var entry in reply.Entries)
        {
            foreach (var field in entry.Fields) fields++;
        }
        return fields;
    }

    /// <summary>
    /// The deferred shape as a <b>consumer</b> actually uses it: every field converted, nothing stored.
    /// </summary>
    /// <remarks>
    /// <b><see cref="DeferredWalk"/> is a floor, not a caller.</b> It increments a counter per field and
    /// never looks at a name or a value, so it measures the traversal skeleton and understates what
    /// reading the data costs. This converts both halves of every field exactly as
    /// <c>ToNameValueEntry</c> does, which makes it the apples-to-apples partner for
    /// <see cref="TransitionalArray"/>: the same per-field work, differing only in whether the results
    /// are put in arrays.
    /// </remarks>
    [Benchmark]
    public async Task<int> DeferredRead()
    {
        using var reply = await _context.Streams.RangeAsync("s").ConfigureAwait(false);
        var seen = 0;
        foreach (var entry in reply.Entries)
        {
            foreach (var field in entry.Fields)
            {
                if (!field.Name.AsRedisValue().IsNull) seen++;
                if (!field.Value.AsRedisValue().IsNull) seen++;
            }
        }
        return seen;
    }

    /// <summary>
    /// The array shape served by a <b>handler</b>, with no reply object and no wrapping async layer.
    /// </summary>
    /// <remarks>
    /// The shape <c>IDatabase.StreamRange</c> actually uses. Against <see cref="TransitionalArray"/> -
    /// the same array, reached by projecting the reply object - this is what supplying a different handler
    /// is worth.
    /// </remarks>
    [Benchmark]
    public async Task<int> HandlerArray()
        => (await _context.Streams.RangeArray("s").ConfigureAwait(false)).Length;

    /// <summary>
    /// The transitional shim: the same reply projected to the array shape <c>IDatabase</c> promises.
    /// </summary>
    [Benchmark]
    public async Task<int> TransitionalArray()
    {
        using var reply = await _context.Streams.RangeAsync("s").ConfigureAwait(false);
        return reply.ToArray().Length;
    }
}
