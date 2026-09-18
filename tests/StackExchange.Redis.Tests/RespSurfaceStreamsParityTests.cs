using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// The stream commands the new surface renders itself, checked against the bytes the <b>classic</b> path
/// produces for the same call.
/// </summary>
/// <remarks>
/// <para>
/// <b>Asserting a string I typed would only prove I read the old code the way I wrote the new code.</b>
/// These drive the real <c>Message</c> builders through <c>MessageWriter</c>, exactly as
/// <see cref="MessageToRespFrameTests"/> does, and compare that against what the interpolated writer
/// emits - so the two independent renderings have to agree about token order, which is the part of
/// <c>XADD</c> that is easy to get wrong and impossible to notice.
/// </para>
/// <para>
/// Byte equality is also not merely tidy: the frame is the client-side cache key, so two routes that
/// disagreed would cache one logical command twice.
/// </para>
/// </remarks>
public class RespSurfaceStreamsParityTests
{
    private sealed class FakeExecutor(string reply) : IRespExecutor
    {
        public List<string> Sent { get; } = [];

        public int Database => 0;

        public RespPayload Send(in RespRequest request)
        {
            Sent.Add(Text(request.Span));
            return RespPayload.Create(Encoding.UTF8.GetBytes(reply));
        }

        public ValueTask<RespPayload> SendAsync(RespRequest request, CancellationToken cancellationToken = default)
            => new(Send(request));
    }

    private static string Text(ReadOnlySpan<byte> value) =>
        Encoding.UTF8.GetString(value.ToArray()).Replace("\r\n", "|");

    /// <summary>The bytes the shipped <c>Message</c> pipeline writes.</summary>
    /// <remarks>
    /// The multiplexer is never touched while building a message - only <c>Database</c> is read, and that
    /// is an <see cref="int"/> on the instance - so a null one is enough to reach the builders. That is
    /// what keeps this a unit test rather than something needing a server.
    /// </remarks>
    private static string Classic(Func<RedisDatabase, Message> build)
    {
        var writer = new RespFrameWriter();
        build(new RedisDatabase(null!, 0, null)).WriteTo(new MessageWriter(null, CommandMap.Default, writer));
        using var frame = writer.Complete(ServerSelectionStrategy.NoSlot);
        return Text(frame.Span);
    }

    /// <summary>The bytes the context surface writes.</summary>
    /// <remarks>
    /// <paramref name="reply"/> has to be a shape the command's handler accepts - the send is driven to
    /// completion so that a parse failure is a test failure rather than a silently swallowed one, which
    /// is how the first version of this file reported "requires a scalar element" instead of a diff.
    /// </remarks>
    private static string Modern(Func<RespDatabaseContext, ValueTask> send, string reply)
    {
        var executor = new FakeExecutor(reply);
        var task = send(new RespDatabaseContext(new RespContext().WithExecutor(executor)));
        Assert.True(task.IsCompleted); // the fake is synchronous; anything else means a stray await
        task.GetAwaiter().GetResult();
        return Assert.Single(executor.Sent);
    }

    private static void AssertSame(Func<RedisDatabase, Message> classic, Func<RespDatabaseContext, ValueTask> modern, string reply)
        => Assert.Equal(Classic(classic), Modern(modern, reply));

    private const string BulkReply = "$3\r\n1-1\r\n";     // XADD: the new entry's id
    private const string IntReply = ":1\r\n";              // XNACK: how many were released
    private const string TrimArrayReply = "*2\r\n:1\r\n:1\r\n"; // XACKDEL: one outcome per id
    private const string OkReply = "+OK\r\n";              // XCFGSET
    private const string AutoClaimReply =                // XAUTOCLAIM: cursor, entries, deleted ids
        "*3\r\n$3\r\n0-0\r\n*1\r\n*2\r\n$3\r\n1-1\r\n*2\r\n$1\r\nf\r\n$1\r\nv\r\n*0\r\n";
    private const string AutoClaimIdsReply =             // XAUTOCLAIM JUSTID
        "*3\r\n$3\r\n0-0\r\n*1\r\n$3\r\n1-1\r\n*0\r\n";
    private const string PendingSummaryReply =           // XPENDING: count, low, high, [[consumer, count]]
        "*4\r\n:2\r\n$3\r\n1-1\r\n$3\r\n9-9\r\n*1\r\n*2\r\n$3\r\nbob\r\n$1\r\n2\r\n";
    private const string PendingMessagesReply =          // XPENDING extended: [id, consumer, idle, deliveries]
        "*1\r\n*4\r\n$3\r\n1-1\r\n$3\r\nbob\r\n:1234\r\n:3\r\n";
    private const string IdsReply = "*1\r\n$3\r\n1-1\r\n";     // XCLAIM JUSTID: a flat run of ids
    private const string EntriesReply =                  // XCLAIM: one entry, one field
        "*1\r\n*2\r\n$3\r\n1-1\r\n*2\r\n$1\r\nf\r\n$1\r\nv\r\n";

    public static TheoryData<string, StreamAddOptions> AddOptions() => new()
    {
        { "bare", default },
        { "explicit id", new StreamAddOptions { MessageId = "5-5" } },
        { "maxlen", new StreamAddOptions { MaxLength = 100 } },
        { "maxlen approx", new StreamAddOptions { MaxLength = 100, Approximate = true } },
        { "maxlen approx limit", new StreamAddOptions { MaxLength = 100, Approximate = true, Limit = 7 } },
        { "minid", new StreamAddOptions { MinId = "3-3" } },
        { "minid approx limit", new StreamAddOptions { MinId = "3-3", Approximate = true, Limit = 7 } },
        { "trim delref", new StreamAddOptions { MaxLength = 5, TrimMode = StreamTrimMode.DeleteReferences } },
        { "trim acked", new StreamAddOptions { MaxLength = 5, TrimMode = StreamTrimMode.Acknowledged } },
        { "nomkstream", new StreamAddOptions { CreateStream = false } },
        { "idmpauto", new StreamAddOptions { IdempotentId = new StreamIdempotentId("p1") } },
        { "idmp", new StreamAddOptions { IdempotentId = new StreamIdempotentId("p1", "i1") } },
        { "everything", new StreamAddOptions { MessageId = "9-9", MaxLength = 100, Approximate = true, Limit = 7, TrimMode = StreamTrimMode.Acknowledged, CreateStream = false, IdempotentId = new StreamIdempotentId("p1", "i1") } },
    };

    [Theory]
    [MemberData(nameof(AddOptions))]
    public void AddWithOneFieldMatches(string name, StreamAddOptions options)
    {
        _ = name; // names the case in the test output
        AssertSame(
            db => db.GetStreamAddMessage("s", in options, new NameValueEntry("f", "v"), CommandFlags.None),
            ctx => Discard(ctx.Streams.AddAsync("s", "f", "v", in options)),
            BulkReply);
    }

    [Theory]
    [MemberData(nameof(AddOptions))]
    public void AddWithSeveralFieldsMatches(string name, StreamAddOptions options)
    {
        _ = name;
        NameValueEntry[] fields = [new("f1", "v1"), new("f2", "v2"), new("f3", "v3")];
        AssertSame(
            db => db.GetStreamAddMessage("s", in options, fields, CommandFlags.None),
            ctx => Discard(ctx.Streams.AddAsync("s", fields, in options)),
            BulkReply);
    }

    [Theory]
    [InlineData(StreamNackMode.Silent)]
    [InlineData(StreamNackMode.Fail)]
    [InlineData(StreamNackMode.Fatal)]
    public void NegativeAcknowledgeOneMatches(StreamNackMode mode)
        => AssertSame(
            db => db.GetStreamNegativeAcknowledgeMessage("s", "g", mode, "1-1", CommandFlags.None),
            ctx => Discard(ctx.Streams.NegativeAcknowledgeAsync("s", "g", mode, "1-1")),
            IntReply);

    [Theory]
    [InlineData(StreamNackMode.Silent)]
    [InlineData(StreamNackMode.Fatal)]
    public void NegativeAcknowledgeManyMatches(StreamNackMode mode)
    {
        RedisValue[] ids = ["1-1", "2-2", "3-3"];
        AssertSame(
            db => db.GetStreamNegativeAcknowledgeMessage("s", "g", mode, ids, CommandFlags.None),
            ctx => Discard(ctx.Streams.NegativeAcknowledgeAsync("s", "g", mode, ids)),
            IntReply);
    }

    [Theory]
    [InlineData(StreamTrimMode.KeepReferences)]
    [InlineData(StreamTrimMode.DeleteReferences)]
    [InlineData(StreamTrimMode.Acknowledged)]
    public void AcknowledgeAndDeleteMatches(StreamTrimMode mode)
    {
        RedisValue[] ids = ["1-1", "2-2"];
        AssertSame(
            db => db.GetStreamAcknowledgeAndDeleteMessage("s", "g", mode, ids, CommandFlags.None),
            ctx => Discard(ctx.Streams.AcknowledgeAndDeleteAsync("s", "g", mode, ids)),
            TrimArrayReply);
    }

    /// <summary>
    /// And the single-id form, which the new surface deliberately does not have.
    /// </summary>
    /// <remarks>
    /// <c>IDatabase</c> keeps it, so the adapter serves it from a one-element span - which only works if
    /// that renders what the old single-id builder rendered. It does, because the old one writes
    /// <c>IDS 1</c> too.
    /// </remarks>
    [Fact]
    public void AcknowledgeAndDeleteSingleMatchesTheOneElementSpan()
        => AssertSame(
            db => db.GetStreamAcknowledgeAndDeleteMessage("s", "g", StreamTrimMode.Acknowledged, "1-1", CommandFlags.None),
            ctx => Discard(ctx.Streams.AcknowledgeAndDeleteAsync("s", "g", StreamTrimMode.Acknowledged, [(RedisValue)"1-1"])),
            TrimArrayReply);

    [Theory]
    [InlineData(null, null)]
    [InlineData(1000L, null)]
    [InlineData(null, 64L)]
    [InlineData(1000L, 64L)]
    public void ConfigureMatches(long? duration, long? maxSize)
    {
        var configuration = new StreamConfiguration { IdmpDuration = duration, IdmpMaxSize = maxSize };
        AssertSame(
            db => db.GetStreamConfigureMessage("s", configuration, CommandFlags.None),
            ctx => ctx.Streams.ConfigureAsync("s", configuration),
            OkReply);
    }

    [Fact]
    public void ClaimMatches()
    {
        RedisValue[] ids = ["1-1", "2-2"];
        AssertSame(
            db => db.GetStreamClaimMessage("s", "g", "c", 5000, ids, returnJustIds: false, CommandFlags.None),
            ctx => Discard(ctx.Streams.ClaimAsync("s", "g", "c", TimeSpan.FromSeconds(5), ids)),
            EntriesReply);
    }

    [Fact]
    public void ClaimIdsOnlyMatches()
    {
        RedisValue[] ids = ["1-1", "2-2"];
        AssertSame(
            db => db.GetStreamClaimMessage("s", "g", "c", 5000, ids, returnJustIds: true, CommandFlags.None),
            ctx => Discard(ctx.Streams.ClaimIdsOnlyAsync("s", "g", "c", TimeSpan.FromSeconds(5), ids)),
            IdsReply);
    }

    /// <summary>
    /// A fractional <see cref="TimeSpan"/> truncates, which is what the wire can carry.
    /// </summary>
    /// <remarks>
    /// Worth pinning rather than assuming: the shipped signature takes whole milliseconds, so the only
    /// way the TimeSpan spelling can be wrong is by rounding somewhere the old one could not.
    /// </remarks>
    [Fact]
    public void ClaimTruncatesSubMillisecondIdleTime()
        => AssertSame(
            db => db.GetStreamClaimMessage("s", "g", "c", 1500, [(RedisValue)"1-1"], returnJustIds: false, CommandFlags.None),
            ctx => Discard(ctx.Streams.ClaimAsync("s", "g", "c", TimeSpan.FromTicks(TimeSpan.TicksPerMillisecond * 1500 + 9999), [(RedisValue)"1-1"])),
            EntriesReply);

    [Fact]
    public void PendingSummaryMatches()
        => AssertSame(
            db => Message.Create(0, CommandFlags.None, RedisCommand.XPENDING, (RedisKey)"s", (RedisValue)"g"),
            ctx => Discard(ctx.Streams.PendingAsync("s", "g")),
            PendingSummaryReply);

    public static TheoryData<string, RedisValue, RedisValue?, RedisValue?, long?> PendingMessageCases() => new()
    {
        { "all consumers", RedisValue.Null, null, null, null },
        { "one consumer", "c1", null, null, null },
        { "bounded", RedisValue.Null, "1-1", "9-9", null },
        { "idle filter", RedisValue.Null, null, null, 5000L },
        { "everything", "c1", "1-1", "9-9", 5000L },
    };

    [Theory]
    [MemberData(nameof(PendingMessageCases))]
    public void PendingMessagesMatches(string name, RedisValue consumer, RedisValue? minId, RedisValue? maxId, long? idleMs)
    {
        _ = name;
        AssertSame(
            db => db.GetStreamPendingMessagesMessage("s", "g", minId, maxId, 10, consumer, idleMs, CommandFlags.None),
            ctx => Discard(ctx.Streams.PendingMessagesAsync("s", "g", 10, consumer, minId, maxId, AsIdle(idleMs))),
            PendingMessagesReply);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(25, false)]
    [InlineData(null, true)]
    [InlineData(25, true)]
    public void AutoClaimMatches(int? count, bool idsOnly)
        => AssertSame(
            db => db.GetStreamAutoClaimMessage("s", "g", "c", 5000, "0-0", count, idsOnly, CommandFlags.None),
            ctx => idsOnly
                ? Discard(ctx.Streams.AutoClaimIdsOnlyAsync("s", "g", "c", TimeSpan.FromSeconds(5), "0-0", count))
                : Discard(ctx.Streams.AutoClaimAsync("s", "g", "c", TimeSpan.FromSeconds(5), "0-0", count)),
            idsOnly ? AutoClaimIdsReply : AutoClaimReply);

    private static TimeSpan? AsIdle(long? ms) => ms.HasValue ? TimeSpan.FromMilliseconds(ms.GetValueOrDefault()) : null;

    private static async ValueTask Discard<T>(ValueTask<T> pending)
    {
        var value = await pending;
        (value as IDisposable)?.Dispose();
    }
}
