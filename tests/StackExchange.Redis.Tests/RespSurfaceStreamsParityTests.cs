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
    private sealed class FakeExecutor(string reply) : RespExecutorBase
    {
        public List<string> Sent { get; } = [];

        public override int Database => 0;

        public override RespPayload Send(in RespRequest request)
        {
            Sent.Add(Text(request.Span));
            return RespPayload.Create(Encoding.UTF8.GetBytes(reply));
        }

        public override ValueTask<RespPayload> SendAsync(RespRequest request, CancellationToken cancellationToken = default)
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
    private const string NamedEntriesReply =             // XREAD: [[name, [entries]]]
        "*1\r\n*2\r\n$1\r\ns\r\n*1\r\n*2\r\n$3\r\n1-1\r\n*2\r\n$1\r\nf\r\n$1\r\nv\r\n";
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

    [Theory]
    [InlineData(null)]
    [InlineData(5)]
    public void ReadMatches(int? count)
        => AssertSame(
            db => db.GetSingleStreamReadMessage("s", StreamPosition.Resolve("0-0", RedisCommand.XREAD), count, CommandFlags.None),
            ctx => Discard(ctx.Streams.ReadAsync("s", "0-0", count)),
            NamedEntriesReply);

    public static TheoryData<string, int?, bool, long?> ReadGroupCases() => new()
    {
        { "bare", null, false, null },
        { "count", 5, false, null },
        { "noack", null, true, null },
        { "claim", null, false, 5000L },
        { "everything", 5, true, 5000L },
    };

    [Theory]
    [MemberData(nameof(ReadGroupCases))]
    public void ReadGroupMatches(string name, int? count, bool noAck, long? claimMs)
    {
        _ = name;
        var claim = claimMs.HasValue ? TimeSpan.FromMilliseconds(claimMs.GetValueOrDefault()) : (TimeSpan?)null;
        AssertSame(
            db => db.GetStreamReadGroupMessage("s", "g", "c", StreamPosition.Resolve(StreamPosition.NewMessages, RedisCommand.XREADGROUP), count, noAck, claim, CommandFlags.None),
            ctx => Discard(ctx.Streams.ReadGroupAsync("s", "g", "c", null, count, noAck, claim)),
            NamedEntriesReply);
    }

    /// <summary>
    /// <c>$</c> is refused for <c>XREAD</c>, which does not block, and accepted for <c>XREADGROUP</c>.
    /// </summary>
    /// <remarks>
    /// The shipped rule, from <c>StreamPosition.Resolve</c>; pinned here because the new surface resolves
    /// the position itself rather than being handed an already-resolved one.
    /// </remarks>
    [Fact]
    public void ReadRefusesNewMessagesButReadGroupDoesNot()
    {
        var ctx = new RespDatabaseContext(new RespContext().WithExecutor(new FakeExecutor(NamedEntriesReply)));
        Assert.Throws<InvalidOperationException>(() => ctx.Streams.ReadAsync("s", StreamPosition.NewMessages));

        var viaGroup = Modern(c => Discard(c.Streams.ReadGroupAsync("s", "g", "c", StreamPosition.NewMessages)), NamedEntriesReply);
        Assert.Contains("|$1|>|", viaGroup);
    }

    /// <summary>
    /// A fractional CLAIM is whole milliseconds on both sides now.
    /// </summary>
    /// <remarks>
    /// This case would have failed before the fix in either direction: the shipped writer passed
    /// TimeSpan.TotalMilliseconds - a double - so it put <c>CLAIM 1500.5</c> on the wire, which the server
    /// rejects as not an integer. Both paths truncate now, so the assertion is both that they agree and
    /// that what they agree on is sendable.
    /// </remarks>
    [Fact]
    public void ReadGroupClaimIsWholeMilliseconds()
    {
        var claim = TimeSpan.FromTicks((TimeSpan.TicksPerMillisecond * 1500) + 5000);
        var rendered = Modern(ctx => Discard(ctx.Streams.ReadGroupAsync("s", "g", "c", null, null, false, claim)), NamedEntriesReply);

        Assert.Contains("|$5|CLAIM|$4|1500|", rendered);
        Assert.DoesNotContain("1500.5", rendered);

        AssertSame(
            db => db.GetStreamReadGroupMessage("s", "g", "c", StreamPosition.Resolve(StreamPosition.NewMessages, RedisCommand.XREADGROUP), null, false, claim, CommandFlags.None),
            ctx => Discard(ctx.Streams.ReadGroupAsync("s", "g", "c", null, null, false, claim)),
            NamedEntriesReply);
    }

    // ---- XREAD / XREADGROUP across several streams -------------------------------------------------
    // The shape most worth pinning: the wire wants every key and THEN every id, so a rendering that
    // walked the positions once - the obvious way to write it - would interleave them and still look
    // plausible. Two streams, because one cannot tell the two orders apart.

    private const string MultiStreamReply =              // XREAD, two streams: [[name, [entries]], ...]
        "*2\r\n*2\r\n$2\r\ns1\r\n*1\r\n*2\r\n$3\r\n1-1\r\n*2\r\n$1\r\nf\r\n$1\r\nv\r\n"
        + "*2\r\n$2\r\ns2\r\n*1\r\n*2\r\n$3\r\n2-2\r\n*2\r\n$1\r\nf\r\n$1\r\nv\r\n";

    private static StreamPosition[] TwoStreams() => [new("s1", "0-0"), new("s2", "5-5")];

    public static TheoryData<string, int?, int?, int?> MultiReadCases() => new()
    {
        { "bare", null, null, null },
        { "count", 5, null, null },
        { "maxcount", null, 10, null },
        { "maxsize", null, null, 1024 },
        { "everything", 5, 10, 1024 },
    };

    [Theory]
    [MemberData(nameof(MultiReadCases))]
    public void MultiReadMatches(string name, int? countPerStream, int? maxCount, int? maxSize)
    {
        _ = name;
        AssertSame(
            db => db.GetMultiStreamReadMessage(TwoStreams(), countPerStream, CommandFlags.None, maxCount, maxSize),
            ctx => Discard(ctx.Streams.ReadAsync(TwoStreams(), countPerStream, maxCount, maxSize)),
            MultiStreamReply);
    }

    public static TheoryData<string, int?, bool, long?, int?, int?> MultiReadGroupCases() => new()
    {
        { "bare", null, false, null, null, null },
        { "count", 5, false, null, null, null },
        { "noack", null, true, null, null, null },
        { "claim", null, false, 5000L, null, null },
        { "caps", null, false, null, 10, 1024 },
        { "everything", 5, true, 5000L, 10, 1024 },
    };

    [Theory]
    [MemberData(nameof(MultiReadGroupCases))]
    public void MultiReadGroupMatches(string name, int? countPerStream, bool noAck, long? claimMs, int? maxCount, int? maxSize)
    {
        _ = name;
        var claim = AsIdle(claimMs);
        AssertSame(
            db => db.GetMultiStreamReadGroupMessage(TwoStreams(), "g", "c", countPerStream, noAck, claim, CommandFlags.None, maxCount, maxSize),
            ctx => Discard(ctx.Streams.ReadGroupAsync(TwoStreams(), "g", "c", countPerStream, noAck, claim, maxCount, maxSize)),
            MultiStreamReply);
    }

    /// <summary>Every key, then every id - not one pair after another.</summary>
    [Fact]
    public void MultiReadPutsAllKeysBeforeAllIds()
    {
        var rendered = Modern(ctx => Discard(ctx.Streams.ReadAsync(TwoStreams())), MultiStreamReply);
        Assert.EndsWith("|$7|STREAMS|$2|s1|$2|s2|$3|0-0|$3|5-5|", rendered);
    }

    /// <summary>
    /// The multi-stream <c>XREAD</c> accepts <c>$</c> and turns it into <c>&gt;</c>.
    /// </summary>
    /// <remarks>
    /// A shipped bug, pinned rather than fixed. The single-stream overload refuses
    /// <see cref="StreamPosition.NewMessages"/> for <c>XREAD</c>, because <c>$</c> means "entries added
    /// while this blocks" and it does not block. The multi-stream message resolves every position against
    /// <c>XREADGROUP</c> instead, so the same input is accepted and rewritten to <c>&gt;</c> - the
    /// consumer-group "undelivered" token, which plain <c>XREAD</c> does not understand at all. Both
    /// surfaces do the same wrong thing, which is the property this test keeps until they are fixed
    /// together.
    /// </remarks>
    [Fact]
    public void MultiReadAcceptsNewMessagesUnlikeTheSingleStreamOverload()
    {
        StreamPosition[] positions = [new("s1", StreamPosition.NewMessages)];
        AssertSame(
            db => db.GetMultiStreamReadMessage(positions, null, CommandFlags.None),
            ctx => Discard(ctx.Streams.ReadAsync(positions)),
            MultiStreamReply);

        // > , not $ : resolved as though this were XREADGROUP
        Assert.EndsWith("|$7|STREAMS|$2|s1|$1|>|", Modern(ctx => Discard(ctx.Streams.ReadAsync(positions)), MultiStreamReply));

        // and the single-stream overload still refuses it outright
        var ctx = new RespDatabaseContext(new RespContext().WithExecutor(new FakeExecutor(MultiStreamReply)));
        Assert.Throws<InvalidOperationException>(() => ctx.Streams.ReadAsync("s1", StreamPosition.NewMessages));
    }

    /// <summary>An empty run is refused before anything is rented.</summary>
    [Fact]
    public void MultiReadDemandsAtLeastOneStream()
    {
        var ctx = new RespDatabaseContext(new RespContext().WithExecutor(new FakeExecutor(MultiStreamReply)));
        Assert.Throws<ArgumentOutOfRangeException>(() => ctx.Streams.ReadAsync(default(ReadOnlySpan<StreamPosition>)));
        Assert.Throws<ArgumentOutOfRangeException>(() => ctx.Streams.ReadGroupAsync(default(ReadOnlySpan<StreamPosition>), "g", "c"));
    }

    // ---- XINFO -------------------------------------------------------------------------------------

    private const string StreamInfoReply =               // XINFO STREAM: a flat name/value map
        "*6\r\n$6\r\nlength\r\n:2\r\n$15\r\nradix-tree-keys\r\n:1\r\n$6\r\ngroups\r\n:1\r\n";
    private const string GroupInfoReply =                // XINFO GROUPS: one group
        "*1\r\n*6\r\n$4\r\nname\r\n$1\r\ng\r\n$9\r\nconsumers\r\n:1\r\n$7\r\npending\r\n:0\r\n";
    private const string ConsumerInfoReply =             // XINFO CONSUMERS: one consumer
        "*1\r\n*6\r\n$4\r\nname\r\n$1\r\nc\r\n$7\r\npending\r\n:0\r\n$4\r\nidle\r\n:5\r\n";

    [Fact]
    public void StreamInfoMatches() => AssertSame(
        db => Message.Create(db.Database, CommandFlags.None, RedisCommand.XINFO, StreamConstants.Stream, (RedisKey)"s"),
        ctx => Discard(ctx.Streams.InfoAsync("s")),
        StreamInfoReply);

    [Fact]
    public void GroupInfoMatches() => AssertSame(
        db => Message.Create(db.Database, CommandFlags.None, RedisCommand.XINFO, StreamConstants.Groups, (RedisKey)"s"),
        ctx => Discard(ctx.Streams.GroupInfoAsync("s")),
        GroupInfoReply);

    /// <summary>
    /// <c>XINFO CONSUMERS</c> agrees byte for byte, though the two routes get there differently.
    /// </summary>
    /// <remarks>
    /// The shipped message passes the key as a <i>value</i> and routes with <c>CreateInKeySlot</c>; the
    /// context surface writes it as a key. Identical bytes with no prefix in force - which is what this
    /// compares - and the context form is the one that still works under a key prefix. See the remarks on
    /// <c>Streams.ConsumerInfoAsync</c>.
    /// </remarks>
    [Fact]
    public void ConsumerInfoMatches() => AssertSame(
        db => Message.CreateInKeySlot(db.Database, "s", CommandFlags.None, RedisCommand.XINFO, new RedisValue[] { StreamConstants.Consumers, "s", "g" }),
        ctx => Discard(ctx.Streams.ConsumerInfoAsync("s", "g")),
        ConsumerInfoReply);

    /// <summary>A prefixed context prefixes the key <c>XINFO CONSUMERS</c> passes as an argument.</summary>
    /// <remarks>The half the shipped message cannot do; see <c>Streams.ConsumerInfoAsync</c>.</remarks>
    [Fact]
    public void ConsumerInfoAppliesAKeyPrefix()
    {
        var executor = new FakeExecutor(ConsumerInfoReply);
        var ctx = new RespDatabaseContext(new RespContext().WithExecutor(executor)).AppendKeyPrefix("app:");
        Discard(ctx.Streams.ConsumerInfoAsync("s", "g")).GetAwaiter().GetResult();

        Assert.Equal("*4|$5|XINFO|$9|CONSUMERS|$5|app:s|$1|g|", Assert.Single(executor.Sent));
    }

    private static TimeSpan? AsIdle(long? ms) => ms.HasValue ? TimeSpan.FromMilliseconds(ms.GetValueOrDefault()) : null;

    private static async ValueTask Discard<T>(ValueTask<T> pending)
    {
        var value = await pending;
        (value as IDisposable)?.Dispose();
    }
}
