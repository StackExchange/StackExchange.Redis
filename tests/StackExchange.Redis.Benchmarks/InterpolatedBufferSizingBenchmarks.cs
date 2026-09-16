using BenchmarkDotNet.Attributes;
using StackExchange.Redis.Interpolated;

namespace StackExchange.Redis.Benchmarks;

// Is the initial buffer estimate right? Measured, and the answer is yes - leave it alone.
//
// RespRequestBuilder rents HeaderMax + 64 + literalLength + (holes * 24), and ArrayPool quantises to
// power-of-two buckets, so a two-hole GET asks for 126 bytes and gets a 128-byte buffer.
//
// The tempting mistake is to compare the REQUEST to the bucket and conclude a GET has two bytes spare. What
// matters is USAGE against the bucket, and usage is far lower: 14 (reserved header) + 9 ($3\r\nGET\r\n) +
// the key, where the key reserves len + 16 rather than len + 6, because BulkReservation is sized for the
// widest int32 length prefix rather than the actual one. So a GET grows when the key passes ~89 bytes.
//
// Measured on a 7900X, ShortRun, per-hole = 24:
//
//   key    6 bytes -> 29.2 ns      key   96 bytes -> 45.5 ns   <-- the grow-and-copy, +44%
//   key   48 bytes -> 31.6 ns      key  460 bytes -> 55.0 ns
//
// The long keys are a CONTROL, not a target: without them the flat region proves nothing, because a
// benchmark that cannot show a step is not evidence that there is no step.
//
// Conclusion: realistic keys are comfortably inside the initial rent, and raising the per-hole estimate
// would make it worse where it matters. 24 -> 32 takes a two-hole command from a 128-byte bucket to a
// 256-byte one - doubling the rent for the single most common command shape in the library - to buy
// headroom that only pays past ~90-byte keys.
//
// Kept as a regression guard: if the cliff moves down into realistic key sizes, this shows it.
public class InterpolatedBufferSizingBenchmarks
{
    private RespContext _ctx;
    private RedisKey _key;
    private RedisValue _value;

    /// <summary>
    /// Key length. The short values are realistic; the long ones are a control - they MUST cross a bucket
    /// boundary and force a grow-and-copy, so a flat curve over the short ones means something.
    /// </summary>
    [Params(6, 48, 96, 110, 220, 460)]
    public int KeyLength { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _ctx = new RespContext();
        _key = new string('k', KeyLength);
        _value = new string('v', 16);
    }

    /// <summary>GET key - two holes, the tightest case.</summary>
    [Benchmark(Baseline = true)]
    public int Get()
    {
        using var frame = _ctx.Render(RedisCommand.GET, $"{_key}");
        return frame.ArgCount;
    }

    /// <summary>SET key value - three holes, one bucket up with room to spare.</summary>
    [Benchmark]
    public int Set()
    {
        using var frame = _ctx.Render(RedisCommand.SET, $"{_key} {_value}");
        return frame.ArgCount;
    }
}
