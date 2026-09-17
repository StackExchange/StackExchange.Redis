using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Scripting on the new surface: SCRIPT LOAD and EVALSHA, sent as a unit.
/// </summary>
/// <remarks>
/// The interesting property is not the bytes but the pairing - that two frames reach the connection
/// together, each still a pure function of its own arguments. That is the mechanism transactions and
/// <c>HIMPORT</c> need too, which is why this is worth proving on the simplest of the three.
/// </remarks>
public class RespSurfaceScriptsTests
{
    /// <summary>An executor with no preamble support: exercises the sequential fallback.</summary>
    private class FakeExecutor(params string[] replies) : IRespExecutor
    {
        private int _next;

        public List<string> Sent { get; } = [];

        public int Database => 0;

        /// <summary>Requests this executor retained, as a backlog awaiting a resend would.</summary>
        public List<RespRequest> Parked { get; } = [];

        public bool ParkRequests { get; set; }

        public RespPayload Send(in RespRequest request)
        {
            Sent.Add(Encoding.UTF8.GetString(request.Span.ToArray()).Replace("\r\n", "|"));
            if (ParkRequests && request.TryRetain(out var retained)) Parked.Add(retained);
            return RespPayload.Create(Encoding.UTF8.GetBytes(replies[Math.Min(_next++, replies.Length - 1)]));
        }

        public ValueTask<RespPayload> SendAsync(RespRequest request, CancellationToken cancellationToken = default)
            => new(Send(request));
    }

    /// <summary>An executor that <i>can</i> pair them, recording that it was asked to.</summary>
    private sealed class PairingExecutor(params string[] replies) : FakeExecutor(replies), IRespPreambleExecutor
    {
        public int Pairs { get; private set; }

        /// <summary>The gate the surface handed over, so tests can assert one was supplied at all.</summary>
        public IRespPreambleGate? Gate { get; private set; }

        public ValueTask<RespPayload> SendAsync(RespRequest preamble, RespRequest request, IRespPreambleGate? gate, CancellationToken cancellationToken = default)
        {
            Pairs++;
            Gate = gate;

            // deliberately NOT consulted here: the gate asks about a PhysicalConnection, which a fake
            // executor does not have. Skipping is decided in the message pipeline, and is tested there.
            Send(preamble).Release();   // the preamble's reply is consumed and discarded
            return new(Send(request));
        }
    }

    private const string Script = "return redis.call('GET', KEYS[1])";

    // sha1("return redis.call('GET', KEYS[1])")
    private const string Sha = "d3c21d0c2b9ca22f82737626a27bcaf5d288f99f";

    [Fact]
    public async Task ThePairIsScriptLoadThenEvalSha()
    {
        var executor = new PairingExecutor("+OK\r\n", "$3\r\nabc\r\n");
        var ctx = new RespContext().WithExecutor(executor);

        using var result = await ctx.Scripts.EvaluateAsync(Script, [(RedisKey)"k"], [(RedisValue)"a"]);

        Assert.Equal(1, executor.Pairs); // written as a unit, not as two independent sends
        Assert.NotNull(executor.Gate);   // and with the means to skip the preamble once it is redundant
        Assert.Equal(2, executor.Sent.Count);
        Assert.StartsWith("*3|$6|SCRIPT|$4|LOAD|", executor.Sent[0]);
        Assert.Equal($"*5|$7|EVALSHA|$40|{Sha}|$1|1|$1|k|$1|a|", executor.Sent[1]);
    }

    /// <summary>
    /// The body goes on the wire once, not twice.
    /// </summary>
    /// <remarks>
    /// This is what computing the hash locally buys. The existing <c>Message</c> path has no hash until
    /// <c>SCRIPT LOAD</c> answers, so its first call sends <c>SCRIPT LOAD</c> and then <c>EVAL</c>, carrying
    /// the script twice.
    /// </remarks>
    [Fact]
    public async Task TheScriptBodyIsSentOnce()
    {
        var executor = new PairingExecutor("+OK\r\n", "$3\r\nabc\r\n");
        var ctx = new RespContext().WithExecutor(executor);

        using var result = await ctx.Scripts.EvaluateAsync(Script);

        var occurrences = 0;
        foreach (var sent in executor.Sent)
        {
            if (sent.Contains("KEYS[1]")) occurrences++;
        }

        Assert.Equal(1, occurrences);
        Assert.DoesNotContain("EVAL|", executor.Sent[1]); // EVALSHA, never a bare EVAL
    }

    /// <summary>
    /// An executor that cannot pair still gets the right commands, in the right order.
    /// </summary>
    /// <remarks>
    /// The capability is detected, not required - the same shape as <c>IRespPayloadHandler</c>. Without the
    /// fallback every fake in the suite would have had to grow a method for a feature it does not use.
    /// </remarks>
    [Fact]
    public async Task AnExecutorWithoutPairingStillSendsBothInOrder()
    {
        var executor = new FakeExecutor("+OK\r\n", "$3\r\nabc\r\n");
        var ctx = new RespContext().WithExecutor(executor);

        using var result = await ctx.Scripts.EvaluateAsync(Script, [(RedisKey)"k"]);

        Assert.Equal(2, executor.Sent.Count);
        Assert.StartsWith("*3|$6|SCRIPT|$4|LOAD|", executor.Sent[0]);
        Assert.StartsWith("*4|$7|EVALSHA|", executor.Sent[1]);
    }

    /// <summary>The keys route the command, exactly as for any other keyed request.</summary>
    /// <remarks>
    /// The pair routes by the <i>request</i>: <c>SCRIPT LOAD</c> names no key, so left alone it would go
    /// anywhere. That is the reason to compose the two rather than inject one at the connection.
    /// </remarks>
    [Fact]
    public async Task TheKeysAreMarkedOnTheRequest()
    {
        var executor = new PairingExecutor("+OK\r\n", "$3\r\nabc\r\n");
        var ctx = new RespContext().WithExecutor(executor);

        using var result = await ctx.Scripts.EvaluateAsync(Script, [(RedisKey)"k1", (RedisKey)"k2"]);

        Assert.Equal($"*5|$7|EVALSHA|$40|{Sha}|$1|2|$2|k1|$2|k2|", executor.Sent[1]);
    }

    /// <summary>A key prefix reaches the script's keys, so keyspace isolation is not quietly bypassed.</summary>
    [Fact]
    public async Task KeysArePrefixedLikeAnyOthers()
    {
        var executor = new PairingExecutor("+OK\r\n", "$3\r\nabc\r\n");
        var ctx = new RespContext().WithExecutor(executor).AppendKeyPrefix("t:");

        using var result = await ctx.Scripts.EvaluateAsync(Script, [(RedisKey)"k"]);

        Assert.Contains("$3|t:k|", executor.Sent[1]);
    }
    /// <summary>
    /// Neither frame's buffer goes back to the pool while the send still owns it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is a regression test for a real bug, and for how badly it presented.</b> The send helper
    /// originally took the two frames <i>by value</i>. <c>RespRequestFrame</c> is a struct and <c>Detach</c> moves
    /// ownership by nulling its own field - so it emptied the copy, the caller's frame still pointed at the
    /// same pooled array, and the caller's <c>finally</c> returned it mid-flight. The array was then
    /// re-rented and overwritten by unrelated work, and the corruption surfaced as
    /// <c>"Unexpected response to ZRANGE"</c> in tests that have nothing to do with scripting.
    /// </para>
    /// <para>
    /// A whole-suite desynchronisation is the worst possible detector: slow, non-deterministic, and it
    /// blames innocent tests. This reproduces the same fault directly - park a reference to each request the
    /// way a backlog would, churn the pool hard enough to hand out anything that was wrongly returned, and
    /// check the bytes are still the ones we sent.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheSendKeepsOwnershipOfBothBuffers()
    {
        var executor = new PairingExecutor("+OK\r\n", "$3\r\nabc\r\n") { ParkRequests = true };
        var ctx = new RespContext().WithExecutor(executor);

        using (var result = await ctx.Scripts.EvaluateAsync(Script, [(RedisKey)"k"]))
        {
            Assert.Equal(2, executor.Parked.Count);
        }

        var before = new List<string>();
        foreach (var parked in executor.Parked)
        {
            before.Add(Encoding.UTF8.GetString(parked.Span.ToArray()));
        }

        // churn the pool: anything wrongly returned is handed out again and stamped on
        for (var i = 0; i < 64; i++)
        {
            var scratch = System.Buffers.ArrayPool<byte>.Shared.Rent(256);
            scratch.AsSpan().Fill(0xDD);
            System.Buffers.ArrayPool<byte>.Shared.Return(scratch);
        }

        for (var i = 0; i < executor.Parked.Count; i++)
        {
            Assert.Equal(before[i], Encoding.UTF8.GetString(executor.Parked[i].Span.ToArray()));
            executor.Parked[i].Dispose();
        }
    }
    /// <summary>
    /// The script body is encoded once, however often it is evaluated.
    /// </summary>
    /// <remarks>
    /// The whole point of the registry: rendering <c>SCRIPT LOAD &lt;body&gt;</c> is a pure function of the
    /// body, so doing it per call is work that can only produce the same answer.
    /// </remarks>
    [Fact]
    public async Task TheBodyIsRenderedOnce()
    {
        var executor = new PairingExecutor("+OK\r\n", "$3\r\nabc\r\n");
        var registry = new RespScriptCache();
        var ctx = new RespContext().WithExecutor(executor).WithScriptCache(registry);

        for (var i = 0; i < 5; i++)
        {
            (await ctx.Scripts.EvaluateAsync(Script, [(RedisKey)"k"])).Dispose();
        }

        Assert.Equal(1, registry.Rendered);
        Assert.Equal(1, registry.Count);
        Assert.Equal(5, executor.Pairs);

        // and every call really did send the same preamble bytes
        for (var i = 0; i < executor.Sent.Count; i += 2)
        {
            Assert.Equal(executor.Sent[0], executor.Sent[i]);
        }
    }

    /// <summary>
    /// The cached rendering is a right-sized array, not the pooled rent it was made from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A rent kept indefinitely is worse than wasteful: it is permanently removed from the pool, degrading
    /// it for everything else. The entry should therefore be an exact copy, with the rent handed straight
    /// back.
    /// </para>
    /// <para>
    /// <b>Asserted on the size, because that is the only thing that distinguishes the two.</b> Surviving
    /// pool churn does not - a correctly retained rent survives churn as well - so an earlier version of
    /// this test proved nothing it claimed. A rent is bucket-sized; a copy is exactly the bytes that went
    /// on the wire.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheCachedRenderingIsExactlySized()
    {
        var executor = new PairingExecutor("+OK\r\n", "$3\r\nabc\r\n");
        var registry = new RespScriptCache();
        var ctx = new RespContext().WithExecutor(executor).WithScriptCache(registry);

        (await ctx.Scripts.EvaluateAsync(Script)).Dispose();

        // the fake records the frame with CRLF collapsed to '|'; restore it to recover the true length
        var onTheWire = executor.Sent[0].Replace("|", "\r\n").Length;

        Assert.Equal(1, registry.Count);
        Assert.Equal(onTheWire, registry.Bytes);
    }

    /// <summary>
    /// <c>NoScriptCache</c> sends EVAL with the body and is not admitted to the registry.
    /// </summary>
    /// <remarks>
    /// The flag says the caller considers this script not worth keeping, and the server agrees - since 7.4
    /// it evicts EVAL-loaded scripts first. Retaining its rendering would be the one way an application
    /// generating scripts per call could grow this without limit, which is the documented anti-pattern.
    /// </remarks>
    [Fact]
    public async Task NoScriptCacheSendsEvalAndIsNotRetained()
    {
        var executor = new PairingExecutor("$3\r\nabc\r\n");
        var registry = new RespScriptCache();
        var ctx = new RespContext().WithExecutor(executor).WithScriptCache(registry);

        (await ctx.Scripts.EvaluateAsync(Script, [(RedisKey)"k"], flags: CommandFlags.NoScriptCache)).Dispose();

        Assert.Equal(0, executor.Pairs);       // one command, no preamble
        Assert.Single(executor.Sent);
        Assert.StartsWith("*4|$4|EVAL|", executor.Sent[0]);
        Assert.Contains("KEYS[1]", executor.Sent[0]);
        Assert.Equal(0, registry.Count);       // nothing retained
    }

    /// <summary>Without a registry the command still works; it just renders each time.</summary>
    [Fact]
    public async Task NoRegistryStillWorks()
    {
        var executor = new PairingExecutor("+OK\r\n", "$3\r\nabc\r\n");
        var ctx = new RespContext().WithExecutor(executor);

        (await ctx.Scripts.EvaluateAsync(Script, [(RedisKey)"k"])).Dispose();
        (await ctx.Scripts.EvaluateAsync(Script, [(RedisKey)"k"])).Dispose();

        Assert.Equal(2, executor.Pairs);
        Assert.Equal(executor.Sent[0], executor.Sent[2]);
    }
}
