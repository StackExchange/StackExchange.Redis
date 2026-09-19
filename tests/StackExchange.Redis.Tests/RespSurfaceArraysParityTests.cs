using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// <c>ARGREP</c>, rendered by the new surface and checked against the classic bytes.
/// </summary>
/// <remarks>
/// <inheritdoc cref="RespSurfaceStreamsParityTests" path="/remarks/para[1]"/>
/// <para>
/// This one earns it more than most: <see cref="ArrayGrepRequest"/> is a builder whose argument list is
/// <i>data</i> - bounds that swap when the request is reversed, a predicate list in insertion order, and
/// four optional switches - and it now has two writers, one per pipeline. Neither can be derived from the
/// other (a <c>ref struct</c> handler and a class writer share no interface), so agreement has to be
/// asserted rather than arranged.
/// </para>
/// </remarks>
public class RespSurfaceArraysParityTests
{
    private sealed class FakeExecutor(string reply) : IRespExecutor
    {
        public string? Sent { get; private set; }

        public int Database => 0;

        public RespPayload Send(in RespRequest request)
        {
            Sent = Text(request.Span);
            return RespPayload.Create(Encoding.UTF8.GetBytes(reply));
        }

        public ValueTask<RespPayload> SendAsync(RespRequest request, CancellationToken cancellationToken = default)
            => new(Send(request));
    }

    private static string Text(ReadOnlySpan<byte> value) =>
        Encoding.UTF8.GetString(value.ToArray()).Replace("\r\n", "|");

    private static string Classic(Func<RedisDatabase, Message> build)
    {
        var writer = new RespFrameWriter();
        build(new RedisDatabase(null!, 0, null)).WriteTo(new MessageWriter(null, CommandMap.Default, writer));
        using var frame = writer.Complete(ServerSelectionStrategy.NoSlot);
        return Text(frame.Span);
    }

    private static string Modern(Func<RespDatabaseContext, ValueTask> send, string reply)
    {
        var executor = new FakeExecutor(reply);
        var task = send(new RespDatabaseContext(new RespContext().WithExecutor(executor)));
        Assert.True(task.IsCompleted); // the fake is synchronous; anything else means a stray await
        task.GetAwaiter().GetResult();
        return executor.Sent!;
    }

    private const string IndicesReply = "*2\r\n:1\r\n:4\r\n";                          // ARGREP: bare indices
    private const string EntriesReply = "*2\r\n:1\r\n$1\r\na\r\n";                    // ARGREP WITHVALUES

    /// <summary>Each request shape, built once and rendered by both writers.</summary>
    /// <remarks>
    /// A builder rather than a value, so each case has to construct its own - a shared instance would be
    /// frozen by the first render.
    /// </remarks>
    public static TheoryData<string> Cases() => new()
    {
        "bare", "bounds", "reversed", "one predicate", "several predicates",
        "and", "nocase", "withvalues", "limit", "everything",
    };

    private static ArrayGrepRequest Build(string which)
    {
        var request = new ArrayGrepRequest();
        switch (which)
        {
            case "bare":
                request.AddPredicate(ArrayGrepRequest.Predicate.Exact(1));
                break;
            case "bounds":
                request.Start = 2;
                request.End = 9;
                request.AddPredicate(ArrayGrepRequest.Predicate.Exact(1));
                break;
            case "reversed":
                request.Start = 2;
                request.End = 9;
                request.IsReversed = true;
                request.AddPredicate(ArrayGrepRequest.Predicate.Exact(1));
                break;
            case "one predicate":
                request.AddPredicate(ArrayGrepRequest.Predicate.Glob("a*"));
                break;
            case "several predicates":
                request.AddPredicate(ArrayGrepRequest.Predicate.Exact(1));
                request.AddPredicate(ArrayGrepRequest.Predicate.Match("b?"));
                request.AddPredicate(ArrayGrepRequest.Predicate.Glob("c*"));
                request.AddPredicate(ArrayGrepRequest.Predicate.Regex("^d"));
                break;
            case "and":
                request.AddPredicate(ArrayGrepRequest.Predicate.Exact(1));
                request.AddPredicate(ArrayGrepRequest.Predicate.Exact(2));
                request.IsIntersection = true;
                break;
            case "nocase":
                request.AddPredicate(ArrayGrepRequest.Predicate.Glob("a*"));
                request.IsCaseInsensitive = true;
                break;
            case "withvalues":
                request.AddPredicate(ArrayGrepRequest.Predicate.Exact(1));
                request.IncludeValues = true;
                break;
            case "limit":
                request.AddPredicate(ArrayGrepRequest.Predicate.Exact(1));
                request.Limit = 7;
                break;
            case "everything":
                request.Start = 2;
                request.End = 9;
                request.IsReversed = true;
                request.AddPredicate(ArrayGrepRequest.Predicate.Exact(1));
                request.AddPredicate(ArrayGrepRequest.Predicate.Regex("^d"));
                request.IsIntersection = true;
                request.IsCaseInsensitive = true;
                request.IncludeValues = true;
                request.Limit = 7;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(which), which, "unknown case");
        }
        return request;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void GrepMatches(string which)
    {
        var reply = Build(which).IncludeValues ? EntriesReply : IndicesReply;
        Assert.Equal(
            Classic(db => Build(which).CreateMessage(db.Database, "k", CommandFlags.None)),
            Modern(ctx => Discard(ctx.Arrays.GrepAsync("k", Build(which))), reply));
    }

    /// <summary>The bounds swap when the request is reversed, and the open ones are <c>-</c> and <c>+</c>.</summary>
    /// <remarks>
    /// Spelled out as well as compared, because a swap that both writers got wrong the same way would
    /// pass <see cref="GrepMatches"/> and still be a wrong command.
    /// </remarks>
    [Fact]
    public void ReversedSwapsTheBoundsAndOpenOnesAreTheTokens()
    {
        var forward = new ArrayGrepRequest { Start = 2 };
        forward.AddPredicate(ArrayGrepRequest.Predicate.Exact(1));
        Assert.Equal(
            "*6|$6|ARGREP|$1|k|$1|2|$1|+|$5|EXACT|$1|1|",
            Modern(ctx => Discard(ctx.Arrays.GrepAsync("k", forward)), IndicesReply));

        var reversed = new ArrayGrepRequest { Start = 2, IsReversed = true };
        reversed.AddPredicate(ArrayGrepRequest.Predicate.Exact(1));
        Assert.Equal(
            "*6|$6|ARGREP|$1|k|$1|+|$1|2|$5|EXACT|$1|1|",
            Modern(ctx => Discard(ctx.Arrays.GrepAsync("k", reversed)), IndicesReply));
    }

    /// <summary><c>WITHVALUES</c> changes the reply's shape, and so which handler reads it.</summary>
    [Fact]
    public async Task IncludeValuesDecidesTheReplyShape()
    {
        var withValues = new ArrayGrepRequest { IncludeValues = true };
        withValues.AddPredicate(ArrayGrepRequest.Predicate.Exact(1));
        var ctx = new RespDatabaseContext(new RespContext().WithExecutor(new FakeExecutor(EntriesReply)));
        using (var pairs = await ctx.Arrays.GrepAsync("k", withValues))
        {
            var entry = Assert.Single(pairs.Span.ToArray());
            Assert.Equal(1UL, entry.Index.Value);
            Assert.Equal("a", entry.Value);
        }

        var indicesOnly = new ArrayGrepRequest();
        indicesOnly.AddPredicate(ArrayGrepRequest.Predicate.Exact(1));
        var bare = new RespDatabaseContext(new RespContext().WithExecutor(new FakeExecutor(IndicesReply)));
        using (var indices = await bare.Arrays.GrepAsync("k", indicesOnly))
        {
            Assert.Equal(2, indices.Length);
            Assert.Equal(1UL, indices.Span[0].Index.Value);
            Assert.Equal(4UL, indices.Span[1].Index.Value);
            Assert.True(indices.Span[0].Value.IsNull); // no value was asked for
        }
    }

    private static async ValueTask Discard<T>(ValueTask<T> pending)
    {
        var value = await pending;
        (value as IDisposable)?.Dispose();
    }
}
