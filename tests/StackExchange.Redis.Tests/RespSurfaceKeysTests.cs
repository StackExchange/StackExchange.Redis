using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// The key-command group: what goes on the wire, and which replies may be cached.
/// </summary>
/// <remarks>
/// The old surface spells these with a <c>Key</c> prefix because one interface had to hold everything;
/// here the receiver is the group. These tests pin the rendered bytes rather than the method names, which
/// is the part a server cares about.
/// </remarks>
public class RespSurfaceKeysTests
{
    private sealed class FakeExecutor(params string[] replies) : IRespExecutor
    {
        private int _next;

        public List<string> Sent { get; } = [];

        public int Database => 0;

        public RespPayload Send(in RespRequest request)
        {
            Sent.Add(Encoding.UTF8.GetString(request.Span.ToArray()).Replace("\r\n", "|"));
            return RespPayload.Create(Encoding.UTF8.GetBytes(replies[Math.Min(_next++, replies.Length - 1)]));
        }

        public ValueTask<RespPayload> SendAsync(RespRequest request, CancellationToken cancellationToken = default)
            => new(Send(request));
    }

    private static (RespContext Context, FakeExecutor Executor) Target(params string[] replies)
    {
        var executor = new FakeExecutor(replies.Length == 0 ? [":1\r\n"] : replies);
        return (new RespContext().WithExecutor(executor), executor);
    }

    [Fact]
    public async Task SingleAndVariadicAreTheSameCommand()
    {
        var (ctx, exec) = Target();

        await ctx.Keys.DeleteAsync("k");
        await ctx.Keys.DeleteAsync([(RedisKey)"a", (RedisKey)"b"]);
        await ctx.Keys.ExistsAsync("k");
        await ctx.Keys.ExistsAsync([(RedisKey)"a", (RedisKey)"b"]);

        Assert.Equal(
            new[]
            {
                "*2|$3|DEL|$1|k|",
                "*3|$3|DEL|$1|a|$1|b|",
                "*2|$6|EXISTS|$1|k|",
                "*3|$6|EXISTS|$1|a|$1|b|",
            },
            exec.Sent);
    }

    /// <summary>The OBJECT family renders as one command with a fixed subcommand token.</summary>
    [Fact]
    public async Task TheObjectFamilyRendersItsSubcommand()
    {
        var (ctx, exec) = Target(":3\r\n", ":3\r\n", ":3\r\n", "$8\r\nlistpack\r\n");

        await ctx.Keys.RefCountAsync("k");
        await ctx.Keys.FrequencyAsync("k");
        await ctx.Keys.IdleTimeAsync("k");
        await ctx.Keys.EncodingAsync("k");

        Assert.Equal(
            new[]
            {
                "*3|$6|OBJECT|$8|REFCOUNT|$1|k|",
                "*3|$6|OBJECT|$4|FREQ|$1|k|",
                "*3|$6|OBJECT|$8|IDLETIME|$1|k|",
                "*3|$6|OBJECT|$8|ENCODING|$1|k|",
            },
            exec.Sent);
    }

    /// <summary>
    /// <c>IDLETIME</c> is <b>seconds</b>, where every other <see cref="TimeSpan"/> on this surface is
    /// milliseconds.
    /// </summary>
    /// <remarks>
    /// The default handler for <c>TimeSpan?</c> reads milliseconds, because <c>PTTL</c> is the common case
    /// - so taking it here would be wrong by a factor of a thousand and entirely plausible-looking. This is
    /// the assertion that says which unit was meant.
    /// </remarks>
    [Fact]
    public async Task IdleTimeIsSecondsNotMilliseconds()
    {
        var (ctx, _) = Target(":3\r\n");

        Assert.Equal(TimeSpan.FromSeconds(3), await ctx.Keys.IdleTimeAsync("k"));
    }

    /// <summary>An empty run is not a command: an arity-zero DEL is a server error, and the answer is zero.</summary>
    [Fact]
    public async Task NoKeysMeansNoCommand()
    {
        var (ctx, exec) = Target();

        Assert.Equal(0, await ctx.Keys.DeleteAsync(ReadOnlySpan<RedisKey>.Empty));
        Assert.Equal(0, await ctx.Keys.ExistsAsync(ReadOnlySpan<RedisKey>.Empty));
        Assert.Equal(0, await ctx.Keys.TouchAsync(ReadOnlySpan<RedisKey>.Empty));
        Assert.Empty(exec.Sent);
    }

    /// <summary>
    /// The expiry decides the command, not the caller.
    /// </summary>
    /// <remarks>
    /// Whether a deadline is absolute or relative, and whether it is seconds or milliseconds, are
    /// properties of the <see cref="Expiration"/> - so one method covers four commands and the caller never
    /// spells a command name.
    /// </remarks>
    [Theory]
    [InlineData("relative-seconds", "*3|$6|EXPIRE|$1|k|$2|60|")]
    [InlineData("relative-millis", "*3|$7|PEXPIRE|$1|k|$5|60500|")]
    [InlineData("absolute-seconds", "*3|$8|EXPIREAT|$1|k|$10|1700000000|")]
    [InlineData("absolute-millis", "*3|$9|PEXPIREAT|$1|k|$13|1700000000500|")]
    public async Task TheExpirationPicksTheCommand(string which, string expected)
    {
        var (ctx, exec) = Target();
        Expiration expiry = which switch
        {
            "relative-seconds" => TimeSpan.FromSeconds(60),
            "relative-millis" => TimeSpan.FromMilliseconds(60_500), // not a whole second: must stay in millis
            // a whole second renders as EXPIREAT; only a fraction forces the millisecond form
            "absolute-seconds" => DateTimeOffset.FromUnixTimeMilliseconds(1700000000000).UtcDateTime,
            _ => DateTimeOffset.FromUnixTimeMilliseconds(1700000000500).UtcDateTime,
        };

        await ctx.Keys.ExpireAsync("k", expiry);
        Assert.Equal(expected, Assert.Single(exec.Sent));
    }

    /// <summary>PERSIST is a command, not an expiry, and saying otherwise is an error rather than a reroute.</summary>
    [Fact]
    public async Task PersistIsNotAnExpiration()
    {
        var (ctx, _) = Target();
        await Assert.ThrowsAsync<ArgumentException>(async () => await ctx.Keys.ExpireAsync("k", Expiration.Persist));
    }

    /// <summary>The optional COPY operands are holes: one command shape covers all four combinations.</summary>
    /// <remarks>
    /// <b>Absent is <see langword="null"/>, not a sentinel.</b> This used to spell "no database" as
    /// <c>-1</c>, which meant the surface could never express a real <c>DB -1</c> and that every optional
    /// number had to pick a value to sacrifice - five of them picked differently. The token now appears
    /// exactly when the value does, because both read the same nullable.
    /// </remarks>
    [Theory]
    [InlineData(null, false, "*3|$4|COPY|$1|a|$1|b|")]
    [InlineData(null, true, "*4|$4|COPY|$1|a|$1|b|$7|REPLACE|")]
    [InlineData(3, false, "*5|$4|COPY|$1|a|$1|b|$2|DB|$1|3|")]
    [InlineData(3, true, "*6|$4|COPY|$1|a|$1|b|$2|DB|$1|3|$7|REPLACE|")]
    [InlineData(0, false, "*5|$4|COPY|$1|a|$1|b|$2|DB|$1|0|")] // zero is a database, not an absence
    public async Task CopyOperandsAreHolesNotBranches(int? db, bool replace, string expected)
    {
        var (ctx, exec) = Target();
        await ctx.Keys.CopyAsync("a", "b", db, replace);
        Assert.Equal(expected, Assert.Single(exec.Sent));
    }

    /// <summary>
    /// An <c>int?</c> hole binds to the nullable overload, not to <see cref="RedisValue"/>.
    /// </summary>
    /// <remarks>
    /// Worth pinning because it is silent either way: the lifted conversion to <c>long?</c> is a standard
    /// conversion and beats the user-defined one to <see cref="RedisValue"/>, so an <c>int?</c> writes
    /// nothing when null. Were it to bind the other way it would write an <i>empty</i> argument, the frame
    /// would stay well-formed, and the server would read a different command.
    /// </remarks>
    [Fact]
    public async Task ANullableHoleWritesNothingRatherThanAnEmptyArgument()
    {
        var (ctx, exec) = Target();
        await ctx.Keys.CopyAsync("a", "b", destinationDatabase: null);

        var sent = Assert.Single(exec.Sent);
        Assert.Equal("*3|$4|COPY|$1|a|$1|b|", sent);
        Assert.DoesNotContain("$0|", sent); // no empty argument
    }

    /// <summary>TYPE reads through the token table, so the wire spellings survive.</summary>
    /// <remarks><c>zset</c> is the case that catches a naive <c>Enum.TryParse</c>.</remarks>
    [Theory]
    [InlineData("+zset\r\n", RedisType.SortedSet)]
    [InlineData("+string\r\n", RedisType.String)]
    [InlineData("+none\r\n", RedisType.None)]
    public async Task TypeIsReadThroughTheTokenTable(string reply, RedisType expected)
    {
        var (ctx, _) = Target(reply);
        Assert.Equal(expected, await ctx.Keys.TypeAsync("k"));
    }

    /// <summary>"No such key" and "no expiry" both read as null; EXISTS is what tells them apart.</summary>
    [Theory]
    [InlineData(":-2\r\n")]
    [InlineData(":-1\r\n")]
    public async Task AbsentDeadlinesReadAsNull(string reply)
    {
        var (ctx, _) = Target(reply);
        Assert.Null(await ctx.Keys.TimeToLiveAsync("k"));

        var (ctx2, _) = Target(reply);
        Assert.Null(await ctx2.Keys.ExpireTimeAsync("k"));
    }

    [Fact]
    public async Task DeadlinesComeBackAsTimeAndInstant()
    {
        var (ctx, _) = Target(":60000\r\n");
        Assert.Equal(TimeSpan.FromSeconds(60), await ctx.Keys.TimeToLiveAsync("k"));

        var (ctx2, _) = Target(":1700000000000\r\n");
        Assert.Equal(
            DateTimeOffset.FromUnixTimeMilliseconds(1700000000000).UtcDateTime,
            await ctx2.Keys.ExpireTimeAsync("k"));
    }

    /// <summary>
    /// The three commands here that must never be cached, and one next to them that must.
    /// </summary>
    /// <remarks>
    /// <c>TOUCH</c> exists for its side effect, so answering it locally skips the only thing it was called
    /// for. <c>RANDOMKEY</c> is meant to differ each time and names no key, so nothing could invalidate it.
    /// <c>PTTL</c> counts down. <c>PEXPIRETIME</c> names an instant and does not drift, which is why it is
    /// the control rather than a fourth exclusion.
    /// </remarks>
    [Fact]
    public async Task TheUncacheableKeyCommandsSaySo()
    {
        static async Task<(bool Cached, long Refused)> Run(string reply, Func<RespContext, ValueTask> go)
        {
            using var cache = new RespClientCache();
            var executor = new FakeExecutor(reply);
            var ctx = new RespContext().WithExecutor(executor).WithCache(cache);
            await go(ctx);
            await go(ctx);
            return (executor.Sent.Count == 1, cache.RefusedByFlags);
        }

        var (touch, touchRefused) = await Run(":1\r\n", static async c => await c.Keys.TouchAsync("k"));
        Assert.False(touch, "TOUCH was served from cache");
        Assert.True(touchRefused > 0);

        var (random, _) = await Run("$1\r\na\r\n", static async c => await c.Keys.RandomAsync());
        Assert.False(random, "RANDOMKEY was served from cache");

        var (ttl, _) = await Run(":60000\r\n", static async c => await c.Keys.TimeToLiveAsync("k"));
        Assert.False(ttl, "PTTL was served from cache");

        // ...and the control: an instant does not drift, so this one is cacheable
        var (when, _) = await Run(":1700000000000\r\n", static async c => await c.Keys.ExpireTimeAsync("k"));
        Assert.True(when, "PEXPIRETIME should be cacheable");

        // the OBJECT family splits the same way, on the same question: can this answer change without the
        // key being WRITTEN? A write is the only thing invalidation reports, so anything else goes stale
        // with nothing to say so. REFCOUNT moves when other keys share an integer; FREQ moves on every
        // read; IDLETIME moves with the clock. ENCODING only changes when the value does - a write - so it
        // is the control here, exactly as PEXPIRETIME is above.
        var (refCount, _) = await Run(":1\r\n", static async c => await c.Keys.RefCountAsync("k"));
        Assert.False(refCount, "OBJECT REFCOUNT was served from cache");

        var (freq, _) = await Run(":1\r\n", static async c => await c.Keys.FrequencyAsync("k"));
        Assert.False(freq, "OBJECT FREQ was served from cache");

        var (idle, _) = await Run(":1\r\n", static async c => await c.Keys.IdleTimeAsync("k"));
        Assert.False(idle, "OBJECT IDLETIME was served from cache");

        var (encoding, _) = await Run("$8\r\nlistpack\r\n", static async c => await c.Keys.EncodingAsync("k"));
        Assert.True(encoding, "OBJECT ENCODING should be cacheable");
    }
}
