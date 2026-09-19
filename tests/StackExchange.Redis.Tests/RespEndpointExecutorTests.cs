using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RESPite.Operations;
using RESPite.Transports;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// The server-endpoint executor: connection lifecycle, reconnection, and the backlog. Design notes
/// section 3b - the executor that owns a connection's whole life rather than just its use.
/// </summary>
public class RespEndpointExecutorTests
{
    /// <summary>A transport that answers on demand and can be killed.</summary>
    private sealed class FakeTransport : DuplexTransport
    {
        private byte[] _out = new byte[1024];
        private int _length;
        private TransportReceiver? _receiver;

        internal string Written => Encoding.UTF8.GetString(_out, 0, _length).Replace("\r\n", "|");

        public override Memory<byte> GetMemory(int sizeHint = 0)
        {
            if (_length + Math.Max(sizeHint, 1) > _out.Length) Array.Resize(ref _out, _length + sizeHint + 1024);
            return _out.AsMemory(_length);
        }

        public override void Advance(int count) => _length += count;

        public override bool Flush() => true;

        public override void Start(TransportReceiver receiver) => _receiver = receiver;

        internal void Reply(string text) => _receiver!.OnReceived(Encoding.UTF8.GetBytes(text));

        internal void Kill(Exception? fault = null) => _receiver!.OnClosed(fault);

        public override ValueTask DisposeAsync() => default;
    }

    /// <summary>Hands out a fresh transport per connect, and remembers them all.</summary>
    private sealed class Endpoint
    {
        internal readonly List<FakeTransport> Transports = [];

        internal int Attempts;

        internal Exception? FailWith;

        internal TaskCompletionSource<bool>? Gate;

        internal async Task<RespConnection> ConnectAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Attempts);
            if (Gate is { } gate) await gate.Task.ConfigureAwait(false);
            if (FailWith is { } fault) throw fault;

            var transport = new FakeTransport();
            lock (Transports) Transports.Add(transport);
            return new RespConnection(transport);
        }

        internal FakeTransport Latest
        {
            get { lock (Transports) return Transports[Transports.Count - 1]; }
        }
    }

    private static RespDatabaseContext Wrap(RespEndpointExecutor executor)
        => new(new RespContext().WithExecutor(executor));

    [Fact]
    public async Task TheFirstSendEstablishesTheConnection()
    {
        // lazily, not eagerly: there is no background loop dialling an endpoint nobody is using
        var endpoint = new Endpoint();
        await using var executor = new RespEndpointExecutor(endpoint.ConnectAsync);
        var context = Wrap(executor);

        Assert.Equal(0, endpoint.Attempts);
        Assert.False(executor.IsConnectedNow);

        var pending = context.Strings.GetAsync("k");
        await WaitFor(() => endpoint.Transports.Count == 1);

        endpoint.Latest.Reply("$5\r\nhello\r\n");
        Assert.Equal("hello", (string?)await pending);
        Assert.Equal(1, executor.Connects);
    }

    [Fact]
    public async Task CommandsArrivingWhileConnectingAreBackloggedAndThenSentInOrder()
    {
        // arrival order is the ordering guarantee: these were queued before anything newer could reach a
        // connection, so replaying them first is what preserves it
        var endpoint = new Endpoint { Gate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        await using var executor = new RespEndpointExecutor(endpoint.ConnectAsync);
        var context = Wrap(executor);

        var first = context.Strings.GetAsync("a");
        var second = context.Strings.GetAsync("b");
        var third = context.Strings.GetAsync("c");

        await WaitFor(() => executor.BacklogCount == 3);
        Assert.Equal(1, endpoint.Attempts); // ONE attempt for three waiters, not three

        endpoint.Gate.SetResult(true);
        await WaitFor(() => endpoint.Transports.Count == 1);

        // wait for the DRAIN to finish, not merely to start: waiting for "any bytes written" and then
        // asserting all three is a race, and one that only shows up when the machine is busy
        const string Expected = "*2|$3|GET|$1|a|*2|$3|GET|$1|b|*2|$3|GET|$1|c|";
        await WaitFor(() => endpoint.Latest.Written.Length >= Expected.Length);

        Assert.Equal(Expected, endpoint.Latest.Written);
        Assert.Equal(0, executor.BacklogCount);

        endpoint.Latest.Reply("$1\r\nA\r\n$1\r\nB\r\n$1\r\nC\r\n");
        Assert.Equal("A", (string?)await first);
        Assert.Equal("B", (string?)await second);
        Assert.Equal("C", (string?)await third);
    }

    [Fact]
    public async Task ALostConnectionIsReplacedOnTheNextSend()
    {
        var endpoint = new Endpoint();
        await using var executor = new RespEndpointExecutor(endpoint.ConnectAsync);
        var context = Wrap(executor);

        var first = context.Strings.GetAsync("a");
        await WaitFor(() => endpoint.Transports.Count == 1);
        endpoint.Latest.Reply("$1\r\nA\r\n");
        Assert.Equal("A", (string?)await first);

        endpoint.Latest.Kill(new InvalidOperationException("connection reset"));
        Assert.False(executor.IsConnectedNow);

        var second = context.Strings.GetAsync("b");
        await WaitFor(() => endpoint.Transports.Count == 2);
        endpoint.Latest.Reply("$1\r\nB\r\n");

        Assert.Equal("B", (string?)await second);
        Assert.Equal(2, executor.Connects);
    }

    [Fact]
    public async Task WhatWasInFlightWhenTheConnectionDiedIsNotSilentlyReplayed()
    {
        // it reached a socket, so it may have been applied; re-sending it is the caller's decision to
        // make through retry, with the flags and the category, not something this layer does behind them
        var endpoint = new Endpoint();
        await using var executor = new RespEndpointExecutor(endpoint.ConnectAsync);
        var context = Wrap(executor);

        var pending = context.Strings.IncrementAsync("counter");
        await WaitFor(() => endpoint.Transports.Count == 1);
        await WaitFor(() => endpoint.Latest.Written.Length > 0);

        endpoint.Latest.Kill(new InvalidOperationException("connection reset"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await pending);
        Assert.Equal("connection reset", ex.Message);
    }

    [Fact]
    public async Task WithoutQueueingADisconnectedSendFailsImmediately()
    {
        var endpoint = new Endpoint { Gate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        await using var executor = new RespEndpointExecutor(endpoint.ConnectAsync, queueWhileDisconnected: false);
        var context = Wrap(executor);

        var ex = await Assert.ThrowsAsync<RedisConnectionException>(async () => await context.Strings.GetAsync("k"));
        Assert.Equal(ConnectionFailureType.SocketClosed, ex.FailureType);
        Assert.Equal(0, endpoint.Attempts); // and it did not even try
    }

    [Fact]
    public async Task ABackloggedCommandKnowsItWasNeverSent()
    {
        // WaitingInBacklog is not decoration: FaultContext.NotApplied reads it, and a command that
        // provably never reached a socket bypasses retry's side-effect cap
        var endpoint = new Endpoint
        {
            Gate = new(TaskCreationOptions.RunContinuationsAsynchronously),
            FailWith = new InvalidOperationException("no route to host"),
        };
        await using var executor = new RespEndpointExecutor(endpoint.ConnectAsync);
        var context = Wrap(executor);

        var pending = context.Strings.GetAsync("k");
        await WaitFor(() => executor.BacklogCount == 1);

        endpoint.Gate.SetResult(true);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await pending);
        Assert.Equal("no route to host", ex.Message);
        Assert.Equal(0, executor.BacklogCount); // failed, not held for a later attempt
    }

    [Fact]
    public async Task AFailedConnectDoesNotStrandTheBacklogForever()
    {
        // holding them would be a queue growing without bound while a server is down; failing them lets
        // the caller - or the retry layer above - decide what to do about it
        var endpoint = new Endpoint
        {
            Gate = new(TaskCreationOptions.RunContinuationsAsynchronously),
            FailWith = new InvalidOperationException("down"),
        };
        await using var executor = new RespEndpointExecutor(endpoint.ConnectAsync);
        var context = Wrap(executor);

        var first = context.Strings.GetAsync("a");
        var second = context.Strings.GetAsync("b");
        await WaitFor(() => executor.BacklogCount == 2);

        endpoint.Gate.SetResult(true);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await first);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await second);

        // and the endpoint is usable again once the server is back: the failure was per-attempt, not
        // a permanent state the executor latched
        endpoint.FailWith = null;
        endpoint.Gate = null;

        var third = context.Strings.GetAsync("c");
        await WaitFor(() => endpoint.Transports.Count == 1);
        endpoint.Latest.Reply("$1\r\nC\r\n");
        Assert.Equal("C", (string?)await third);
    }

    [Fact]
    public async Task DisposingFailsWhatWasWaiting()
    {
        var endpoint = new Endpoint { Gate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var executor = new RespEndpointExecutor(endpoint.ConnectAsync);
        var context = Wrap(executor);

        var pending = context.Strings.GetAsync("k");
        await WaitFor(() => executor.BacklogCount == 1);

        await executor.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await pending);
        endpoint.Gate.SetResult(true);
    }

    /// <remarks>
    /// A stopwatch rather than <c>Environment.TickCount64</c>, and explicit indexing rather than
    /// <c>[^1]</c>: this project targets net481 too, where neither exists.
    /// </remarks>
    private static async Task WaitFor(Func<bool> condition, int millis = 5000)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.ElapsedMilliseconds > millis) Assert.Fail("condition not met within the time allowed");
            await Task.Delay(5);
        }
    }
}
