using System;
using System.Collections.Generic;
using System.Text;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;
using NSubstitute;
using StackExchange.Redis.Interpolated;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// <c>TransitionalDatabase</c>: the old <see cref="IDatabase"/> surface over the new context surface, one
/// command at a time.
/// </summary>
public class TransitionalDatabaseTests
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

    // the multiplexer is only reached to apply a timeout when a send does NOT complete synchronously;
    // the fake always does, so passing null here also pins that the fast path really is the fast path
    private static IDatabase Target(FakeExecutor executor)
        => new TransitionalDatabase(new RespDatabase(new RespContext().WithExecutor(executor)), null!, null);

    [Fact]
    public void AMovedCommandReachesTheContextSurface()
    {
        // THE test for the generator change. Both a hand-written public member and a generated EXPLICIT
        // one compile happily side by side, and interface dispatch silently prefers the generated throw -
        // so nothing here can be caught at build time. Going through IDatabase is what proves the
        // generator stood back.
        var executor = new FakeExecutor("$4\r\nmarc\r\n");
        var db = Target(executor);

        Assert.Equal("marc", (string?)db.StringGet("user:1"));
        Assert.Equal("*2|$3|GET|$6|user:1|", Assert.Single(executor.Sent));
    }

    [Fact]
    public async Task AMovedCommandWorksAsynchronouslyToo()
    {
        var executor = new FakeExecutor("$4\r\nmarc\r\n");
        var db = Target(executor);

        Assert.Equal("marc", (string?)await db.StringGetAsync("user:1"));
    }

    [Fact]
    public void TheFullSetOverloadRendersItsOptionalArguments()
    {
        var executor = new FakeExecutor("+OK\r\n");
        var db = Target(executor);

        Assert.True(db.StringSet("k", "v", TimeSpan.FromSeconds(300), when: When.NotExists));
        Assert.Equal("*6|$3|SET|$1|k|$1|v|$2|NX|$2|EX|$3|300|", Assert.Single(executor.Sent));
    }

    [Fact]
    public void AnUnmovedCommandThrowsAndSaysSo()
    {
        var db = Target(new FakeExecutor("+OK\r\n"));

        // the exemplar has to be a command that genuinely has not moved, so it changes as groups land -
        // KeyDelete was this until the Key group arrived, and ListLeftPush until the List group did.
        // Stream is also what AnUnmovedGroupIsStillGenerated uses as its control, so the two now go stale
        // together and loudly rather than one of them quietly.
        var ex = Assert.Throws<NotImplementedException>(() => db.StreamLength("k"));
        Assert.Contains("has not yet moved", ex.Message);
    }

    [Fact]
    public void AnUnmovedStreamingCommandThrowsToo()
    {
        // the scans are the one part [AutoDatabase] skips by category, so they are hand-written; this is
        // here so that "hand-written" does not quietly become "forgotten"
        var db = Target(new FakeExecutor("+OK\r\n"));

        Assert.Throws<NotImplementedException>(() => db.HashScan("k"));
    }

    /// <summary>A ValueTask source that records whether its result was consumed.</summary>
    private sealed class ConsumptionProbe : IValueTaskSource
    {
        public int GetResultCalls { get; private set; }

        public ValueTaskSourceStatus GetStatus(short token) => ValueTaskSourceStatus.Succeeded;

        public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
            => continuation(state);

        public void GetResult(short token) => GetResultCalls++;
    }

    [Fact]
    public void AResultLessWaitStillConsumesTheValueTask()
    {
        // The rule that is easy to lose: a ValueTask backed by an IValueTaskSource must have its result
        // consumed exactly once, because GetResult(token) is what lets the source complete its lifecycle
        // and be reset or pooled. With no value to take, the call looks droppable - so this is here to
        // fail if someone drops it. Abandoning the source leaks it and can hand a stale token to whoever
        // borrows it next, which is a bug that would surface nowhere near this code.
        var probe = new ConsumptionProbe();
        var db = (TransitionalDatabase)Target(new FakeExecutor("+OK\r\n"));

        var wait = typeof(TransitionalDatabase).GetMethod(
            "Wait",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(ValueTask)],
            modifiers: null);
        Assert.NotNull(wait);

        wait!.Invoke(db, [new ValueTask(probe, token: 0)]);

        Assert.Equal(1, probe.GetResultCalls);
    }

    [Fact]
    public void AsDatabaseCompletesTheRoundTrip()
    {
        // IDatabase.Context already goes new <- legacy; this is the other direction, so neither surface is
        // a one-way door. Note the concrete type stays internal - the contract is IDatabase.
        var executor = new FakeExecutor("$4\r\nmarc\r\n");
        var surface = new RespDatabase(new RespContext().WithExecutor(executor));

        IDatabase legacy = surface.AsDatabase(Substitute.For<IConnectionMultiplexer>());

        Assert.Equal("marc", (string?)legacy.StringGet("user:1"));
        Assert.Equal("*2|$3|GET|$6|user:1|", Assert.Single(executor.Sent));

        // and back again, to the same context
        Assert.Same(executor, ((IRespTarget)legacy).Context.Executor);
    }

    [Fact]
    public void TheWholeInterfaceIsImplemented()
    {
        // the point of the generator: this type satisfies IDatabase in full without anyone listing it
        Assert.True(typeof(IDatabase).IsAssignableFrom(typeof(TransitionalDatabase)));
    }
}
