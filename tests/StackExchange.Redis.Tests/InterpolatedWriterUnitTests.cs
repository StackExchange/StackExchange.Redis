using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using RESPite.Messages;
using StackExchange.Redis.Interpolated;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Unit tests for the experimental interpolated-string RESP writer spike; see
/// design/interpolated-resp-writer.md. Pure formatting/routing - no server involved.
/// </summary>
public class InterpolatedWriterUnitTests
{
    /// <summary>Parse a rendered frame back into its arguments, demanding exact consumption.</summary>
    private static string[] Parse(ReadOnlySpan<byte> frame)
    {
        var reader = new RespReader(frame);
        reader.MoveNext();
        Assert.Equal(RespPrefix.Array, reader.Prefix);
        var count = reader.AggregateLength();
        var args = new string[count];
        for (int i = 0; i < count; i++)
        {
            reader.MoveNext();
            Assert.Equal(RespPrefix.BulkString, reader.Prefix);
            args[i] = reader.ReadString() ?? "";
        }

        reader.DemandEnd(); // no over- or under-run
        return args;
    }

    // sized from KeyCount, NOT a fixed two: a fixed buffer makes TryGetKeys return -1 for "target too
    // small", which is indistinguishable here from "frame cannot report its keys" and would let a test
    // claiming the latter pass for the former reason
    private static string[] Keys(in RespRequestFrame frame)
    {
        var count = frame.KeyCount;
        if (count < 0) return null!; // the frame genuinely cannot report them
        var ranges = new KeyRange[count];
        Assert.Equal(count, frame.TryGetKeys(ranges));
        var keys = new string[count];
        for (int i = 0; i < count; i++) keys[i] = Encoding.UTF8.GetString(frame.GetKey(ranges[i]).ToArray());
        return keys;
    }

    [Fact]
    public void RendersCommandKeyAndValue()
    {
        var ctx = new RespContext();
        using var frame = ctx.Render($"{RedisCommand.SET}{(RedisKey)"mykey"}{(RedisValue)"myvalue"}");

        Assert.Equal(3, frame.ArgCount);
        Assert.Equal(new[] { "SET", "mykey", "myvalue" }, Parse(frame.Span));
    }

    [Fact]
    public void RendersExactBytes()
    {
        var ctx = new RespContext();
        using var frame = ctx.Render($"{RedisCommand.GET}{(RedisKey)"abc"}");

        Assert.Equal("*2\r\n$3\r\nGET\r\n$3\r\nabc\r\n", Encoding.UTF8.GetString(frame.Span.ToArray()));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(9)]
    [InlineData(10)]   // '*NN' - two digits, so the frame start moves
    [InlineData(120)]  // three
    public void HeaderBackfillIsRightAligned(int extraArgs)
    {
        var ctx = new RespContext();
        var handler = new RespCommandHandler(0, extraArgs + 2, ctx);
        handler.AppendFormatted(RedisCommand.SET);
        handler.AppendFormatted((RedisKey)"mykey");
        for (int i = 0; i < extraArgs; i++) handler.AppendFormatted((RedisValue)i);
        using var frame = handler.Complete();

        var args = Parse(frame.Span);
        Assert.Equal(extraArgs + 2, args.Length);
        Assert.Equal("SET", args[0]);

        // the key offset is buffer-absolute, so it survives the header growing
        Assert.Equal(new[] { "mykey" }, Keys(frame));
    }

    [Fact]
    public void CommandMapRenamesAreApplied()
    {
        var map = CommandMap.Create(new Dictionary<string, string?> { ["set"] = "xset" });
        var ctx = new RespContext(map);
        using var frame = ctx.Render($"{RedisCommand.SET}{(RedisKey)"k"}{(RedisValue)"v"}");

        Assert.Equal(new[] { "XSET", "k", "v" }, Parse(frame.Span));
    }

    [Fact]
    public void DisabledCommandThrows()
    {
        var map = CommandMap.Create(new Dictionary<string, string?> { ["set"] = null });
        var ctx = new RespContext(map);

        Assert.Throws<RedisCommandException>(() => ctx.Render($"{RedisCommand.SET}{(RedisKey)"k"}{(RedisValue)"v"}").Dispose());
    }

    [Fact]
    public void CommandMustComeFirst()
    {
        var ctx = new RespContext();
        Assert.Throws<InvalidOperationException>(() => ctx.Render($"{(RedisKey)"k"}{RedisCommand.GET}").Dispose());
    }

    [Fact]
    public void KeyPrefixIsAppliedToTheWire()
    {
        var ctx = new RespContext().AppendKeyPrefix("tenant7:");
        using var frame = ctx.Render($"{RedisCommand.GET}{(RedisKey)"user:1"}");

        Assert.Equal(new[] { "GET", "tenant7:user:1" }, Parse(frame.Span));
        Assert.Equal(new[] { "tenant7:user:1" }, Keys(frame));
    }

    [Fact]
    public void KeyPrefixComposesWithAKeyThatAlreadyHasOne()
    {
        // a key that already carries a prefix, as the KeyPrefixed* decorators produce today
        var prefixed = RedisKey.WithPrefix(Encoding.UTF8.GetBytes("inner:"), "user:1");
        var ctx = new RespContext().AppendKeyPrefix("outer:");
        using var frame = ctx.Render($"{RedisCommand.GET}{prefixed}");

        Assert.Equal(new[] { "GET", "outer:inner:user:1" }, Parse(frame.Span));
    }

    [Fact]
    public void DatabaseIsNotPartOfTheRenderedFrame()
    {
        // SELECT is a separate command on the connection, so the same logical command renders IDENTICALLY
        // on different databases. Cache identity therefore needs (frame, database) - the frame alone is not
        // enough, which is easy to miss because everything else that matters (prefix, renamed command,
        // arguments) IS in the bytes.
        using var a = new RespContext(database: 0).Render($"{RedisCommand.GET}{(RedisKey)"k"}");
        using var b = new RespContext(database: 3).Render($"{RedisCommand.GET}{(RedisKey)"k"}");

        Assert.True(a.Span.SequenceEqual(b.Span));
        Assert.Equal(0, new RespContext(database: 0).Database);
        Assert.Equal(3, new RespContext(database: 3).Database);
    }

    [Fact]
    public void BothPrefixMechanismsRenderIdenticalBytes()
    {
        // decorator-applied prefix (rides on the key) vs context-applied prefix (applied at write time).
        // They are different RedisKey VALUES - RedisKey.Equals compares the carried prefix - but they must
        // be indistinguishable on the wire, which is what lets the rendered frame serve as a cache key.
        var viaDecorator = RedisKey.WithPrefix(Encoding.UTF8.GetBytes("tenant7:"), "user:1");
        using var a = new RespContext().Render($"{RedisCommand.GET}{viaDecorator}");
        using var b = new RespContext(keyPrefix: "tenant7:").Render($"{RedisCommand.GET}{(RedisKey)"user:1"}");

        Assert.True(a.Span.SequenceEqual(b.Span));
        Assert.Equal(new[] { "GET", "tenant7:user:1" }, Parse(a.Span));
    }

#if NET
    [Fact]
    public void ComposingBothPrefixMechanismsDoesNotAllocate()
    {
        // Both mechanisms have to coexist, so this is the permanent hot path - not an interim state.
        // RedisKey.WithPrefix has to allocate to combine two prefixes (its "two prefixes; darn" branch)
        // because it must hand back a RedisKey; the writer only ever needs the combined bytes.
        var decorated = RedisKey.WithPrefix(Encoding.UTF8.GetBytes("inner:"), "user:1");
        var ctx = new RespContext(keyPrefix: "outer:");

        for (int i = 0; i < 64; i++) ctx.Render($"{RedisCommand.GET}{decorated}").Dispose(); // warm the pool

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 128; i++) ctx.Render($"{RedisCommand.GET}{decorated}").Dispose();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated == 0, $"allocated {allocated} bytes over 128 renders");
    }
#endif

    [Fact]
    public void NestedAppendKeyPrefixComposes()
    {
        var ctx = new RespContext().AppendKeyPrefix("a:").AppendKeyPrefix("b:");
        using var frame = ctx.Render($"{RedisCommand.GET}{(RedisKey)"k"}");

        Assert.Equal(new[] { "GET", "a:b:k" }, Parse(frame.Span));
    }

    [Fact]
    public void ChannelPrefixIsApplied()
    {
        var ctx = new RespContext(channelPrefix: new RedisChannel("app:", RedisChannel.PatternMode.Literal));
        using var frame = ctx.Render($"{RedisCommand.PUBLISH}{new RedisChannel("news", RedisChannel.PatternMode.Literal)}{(RedisValue)"hi"}");

        Assert.Equal(new[] { "PUBLISH", "app:news", "hi" }, Parse(frame.Span));
    }

    [Fact]
    public void ChannelPrefixIsSkippedWhenTheChannelOptsOut()
    {
        // keyspace notification channels are server-generated names, and opt out of the channel prefix
        var channel = new RedisChannel("__keyevent@0__:set", RedisChannel.RedisChannelOptions.IgnoreChannelPrefix);
        var ctx = new RespContext(channelPrefix: new RedisChannel("app:", RedisChannel.PatternMode.Literal));
        using var frame = ctx.Render($"{RedisCommand.SUBSCRIBE}{channel}");

        Assert.Equal(new[] { "SUBSCRIBE", "__keyevent@0__:set" }, Parse(frame.Span));
    }

    [Fact]
    public void NoKeysMeansNoSlotAndNoMarks()
    {
        var ctx = new RespContext(serverType: ServerType.Cluster);
        using var frame = ctx.Render($"{RedisCommand.ECHO}{(RedisValue)"hello"}");

        Assert.True(frame.HasNoKeys);
        Assert.Empty(Keys(frame));
        Assert.Equal(ServerSelectionStrategy.NoSlot, frame.Slot);
    }

    [Fact]
    public void OneAndTwoKeysResolveWithoutScanning()
    {
        var ctx = new RespContext();

        using (var one = ctx.Render($"{RedisCommand.GET}{(RedisKey)"k1"}"))
        {
            Assert.False(one.KeysNeedScan);
            Assert.Equal(new[] { "k1" }, Keys(one));
        }

        using var two = ctx.Render($"{RedisCommand.SMOVE}{(RedisKey)"src"}{(RedisKey)"dst"}{(RedisValue)"m"}");
        Assert.False(two.KeysNeedScan);
        Assert.Equal(new[] { "src", "dst" }, Keys(two));
    }

    [Fact]
    public void ThreeKeysResolveViaTheBitmap()
    {
        var ctx = new RespContext();
        using var frame = ctx.Render($"{RedisCommand.DEL}{(RedisKey)"a"}{(RedisKey)"b"}{(RedisKey)"c"}");

        // past the two inline offsets, so resolving needs a walk - but the keys ARE recoverable; the
        // writer records every key's argument index as well as the first two offsets
        Assert.True(frame.KeysNeedScan);
        Assert.Equal(3, frame.KeyCount);
        Assert.Equal(new[] { "a", "b", "c" }, Keys(frame));
        Assert.Equal(new[] { "DEL", "a", "b", "c" }, Parse(frame.Span));
    }

    [Fact]
    public void StandaloneSkipsSlotComputation()
    {
        var ctx = new RespContext(serverType: ServerType.Standalone);
        using var frame = ctx.Render($"{RedisCommand.GET}{(RedisKey)"foo"}");

        Assert.Equal(ServerSelectionStrategy.NoSlot, frame.Slot);
    }

    [Fact]
    public void ClusterFoldsTheSlotFromTheWrittenBytes()
    {
        var ctx = new RespContext(serverType: ServerType.Cluster);
        using var frame = ctx.Render($"{RedisCommand.GET}{(RedisKey)"foo"}");

        // published CLUSTER KEYSLOT value
        Assert.Equal(12182, frame.Slot);
        Assert.Equal(ServerSelectionStrategy.GetHashSlot((RedisKey)"foo"), frame.Slot);
    }

    [Fact]
    public void SharedHashTagGivesOneSlot()
    {
        var ctx = new RespContext(serverType: ServerType.Cluster);
        using var frame = ctx.Render($"{RedisCommand.SMOVE}{(RedisKey)"{u1}:a"}{(RedisKey)"{u1}:b"}{(RedisValue)"m"}");

        Assert.Equal(ServerSelectionStrategy.GetHashSlot((RedisKey)"{u1}:a"), frame.Slot);
        Assert.NotEqual(ServerSelectionStrategy.MultipleSlots, frame.Slot);
    }

    [Fact]
    public void CrossSlotKeysAreDetected()
    {
        var ctx = new RespContext(serverType: ServerType.Cluster);
        using var frame = ctx.Render($"{RedisCommand.SMOVE}{(RedisKey)"alpha"}{(RedisKey)"beta"}{(RedisValue)"m"}");

        Assert.Equal(ServerSelectionStrategy.MultipleSlots, frame.Slot);
    }

    [Fact]
    public void SlotIsComputedFromThePrefixedKey()
    {
        var plain = new RespContext(serverType: ServerType.Cluster);
        var prefixed = plain.AppendKeyPrefix("tenant7:");

        using var a = plain.Render($"{RedisCommand.GET}{(RedisKey)"user:1"}");
        using var b = prefixed.Render($"{RedisCommand.GET}{(RedisKey)"user:1"}");

        Assert.NotEqual(a.Slot, b.Slot);
        Assert.Equal(ServerSelectionStrategy.GetHashSlot((RedisKey)"tenant7:user:1"), b.Slot);
    }

    /// <summary>
    /// A token cancelled before the call is honoured properly, and hands the buffer back.
    /// </summary>
    /// <remarks>
    /// The half of cancellation that works today: an in-flight request cannot be stopped, but declining to
    /// start one costs nothing - so this is an <see cref="OperationCanceledException"/>, not "not
    /// implemented". Checked before the cancellable case, since a cancelled token is also cancellable.
    /// <para>
    /// The handler is built by hand rather than left to the compiler, so the test can still see it after
    /// the throw. That matters: a leak back to <c>ArrayPool</c> is invisible from outside - an empty bucket
    /// just allocates - so renting in a loop and checking nothing broke asserts nothing at all.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnAlreadyCancelledTokenIsHonouredAndReturnsTheBuffer()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var ctx = new RespContext();

        var handler = new RespCommandHandler(0, 1, ctx, RedisCommand.GET);
        handler.AppendFormatted((RedisKey)"k");

        var threw = false;
        try
        {
            _ = ctx.SendAsync<bool>(ref handler, CommandFlags.None, null, cts.Token);
        }
        catch (OperationCanceledException)
        {
            threw = true;
        }

        Assert.True(threw, "an already-cancelled token should refuse to start");
        Assert.True(handler.BufferReturned, "the rented buffer was not handed back");
    }

    /// <summary>A live-but-uncancelled token is refused, and also hands the buffer back.</summary>
    [Fact]
    public void ACancellableTokenIsRefusedRatherThanIgnored()
    {
        using var cts = new CancellationTokenSource(); // live, but NOT cancelled
        var ctx = new RespContext();

        var handler = new RespCommandHandler(0, 1, ctx, RedisCommand.GET);
        handler.AppendFormatted((RedisKey)"k");

        NotImplementedException? caught = null;
        try
        {
            _ = ctx.SendAsync<bool>(ref handler, CommandFlags.None, null, cts.Token);
        }
        catch (NotImplementedException ex)
        {
            caught = ex;
        }

        Assert.NotNull(caught);
        Assert.Contains("not yet supported", caught!.Message);
        Assert.True(handler.BufferReturned, "the rented buffer was not handed back");
    }

    [Fact]
    public void LargePayloadForcesBufferGrowthMidBuild()
    {
        var big = new string('x', 5000);
        var ctx = new RespContext();
        using var frame = ctx.Render($"{RedisCommand.SET}{(RedisKey)"k"}{(RedisValue)big}");

        var args = Parse(frame.Span);
        Assert.Equal(big, args[2]);

        // the reserved prologue and the key offset must survive the regrow
        Assert.Equal(new[] { "k" }, Keys(frame));
    }

    // ---- deferred composition: optional / contextual arguments -------------------------------------

    [Theory]
    [InlineData(false, false, "SET|k|v")]
    [InlineData(true, false, "SET|k|v|EX|300")]
    [InlineData(false, true, "SET|k|v|NX")]
    [InlineData(true, true, "SET|k|v|EX|300|NX")]
    public void ComposeThenConditionallyAppend(bool withTtl, bool withNx, string expected)
    {
        var ctx = new RespContext();
        var cmd = ctx.Compose($"{RedisCommand.SET}{(RedisKey)"k"}{(RedisValue)"v"}");
        try
        {
            if (withTtl)
            {
                cmd.AppendFormatted((RedisValue)"EX");
                cmd.AppendFormatted((RedisValue)300);
            }

            if (withNx) cmd.AppendFormatted((RedisValue)"NX");

            using var frame = ctx.Render(ref cmd);
            Assert.Equal(expected, string.Join("|", Parse(frame.Span)));
            Assert.Equal(expected.Split('|').Length, frame.ArgCount);
        }
        catch
        {
            cmd.Dispose(); // Execute takes ownership on success; this covers the throwing window
            throw;
        }
    }

    [Fact]
    public void ComposedKeysStillTrackAndRoute()
    {
        var ctx = new RespContext(serverType: ServerType.Cluster);
        var cmd = ctx.Compose($"{RedisCommand.SMOVE}{(RedisKey)"{u}:src"}");
        cmd.AppendFormatted((RedisKey)"{u}:dst");   // second key arrives AFTER the interpolation
        cmd.AppendFormatted((RedisValue)"m");
        using var frame = ctx.Render(ref cmd);

        Assert.Equal(new[] { "SMOVE", "{u}:src", "{u}:dst", "m" }, Parse(frame.Span));
        Assert.Equal(new[] { "{u}:src", "{u}:dst" }, Keys(frame));
        Assert.Equal(ServerSelectionStrategy.GetHashSlot((RedisKey)"{u}:src"), frame.Slot);
    }

    [Fact]
    public void ComposedHeaderGrowsWithLateArguments()
    {
        // 3 args at the call site, 12 by the time it is executed: '*3' would have been wrong
        var ctx = new RespContext();
        var cmd = ctx.Compose($"{RedisCommand.SET}{(RedisKey)"k"}{(RedisValue)"v"}");
        for (int i = 0; i < 9; i++) cmd.AppendFormatted((RedisValue)i);
        using var frame = ctx.Render(ref cmd);

        Assert.Equal(12, frame.ArgCount);
        Assert.StartsWith("*12\r\n", Encoding.UTF8.GetString(frame.Span.ToArray()));
        Assert.Equal(new[] { "k" }, Keys(frame)); // offset survived the two-digit header
    }

    // ---- initializing with the command as a real argument -----------------------------------------

    [Fact]
    public void ComposeWithCommandArgument()
    {
        var ctx = new RespContext();
        var cmd = ctx.Compose(RedisCommand.SET, $"{(RedisKey)"k"}{(RedisValue)"v"}");
        using var frame = ctx.Render(ref cmd);

        Assert.Equal(new[] { "SET", "k", "v" }, Parse(frame.Span));
        Assert.Equal(new[] { "k" }, Keys(frame));
    }

    [Fact]
    public void ExecuteWithCommandArgument()
    {
        var ctx = new RespContext(serverType: ServerType.Cluster);
        using var frame = ctx.Render(RedisCommand.GET, $"{(RedisKey)"foo"}");

        Assert.Equal(new[] { "GET", "foo" }, Parse(frame.Span));
        Assert.Equal(12182, frame.Slot);
    }

    [Fact]
    public void ComposeWithNoInterpolationAtAll()
    {
        // fully dynamic argument list - variadic DEL over a runtime-sized set of keys
        var keys = new RedisKey[] { "a", "b", "c" };
        var ctx = new RespContext();
        var cmd = ctx.Compose(RedisCommand.DEL, keys.Length);
        foreach (var key in keys) cmd.AppendFormatted(key);
        using var frame = ctx.Render(ref cmd);

        Assert.Equal(new[] { "DEL", "a", "b", "c" }, Parse(frame.Span));
        Assert.Equal(4, frame.ArgCount);
        Assert.True(frame.KeysNeedScan); // three keys exceeds the two inline offsets
        Assert.Equal(new[] { "a", "b", "c" }, Keys(frame)); // still recoverable, via the bitmap
    }

    [Fact]
    public void DisabledCommandThrowsFromBothInitializerForms()
    {
        // Note the command-as-argument form resolves the map before renting, while the hole form rents
        // first and discovers it on the first append - but a throwing interpolation abandoning its buffer
        // is accepted behaviour either way; DefaultInterpolatedStringHandler does exactly the same.
        var map = CommandMap.Create(new Dictionary<string, string?> { ["get"] = null });
        var ctx = new RespContext(map);

        Assert.Throws<RedisCommandException>(() => ctx.Compose(RedisCommand.GET, 0).Dispose());
        Assert.Throws<RedisCommandException>(() => ctx.Render($"{RedisCommand.GET}{(RedisKey)"k"}").Dispose());
    }

    // ---- the single-space relaxation ---------------------------------------------------------------

    [Fact]
    public void SingleSpacesAreAllowedAndDiscarded()
    {
        var ctx = new RespContext();
        using var spaced = ctx.Render($"{RedisCommand.SET} {(RedisKey)"k"} {(RedisValue)"v"}");
        using var tight = ctx.Render($"{RedisCommand.SET}{(RedisKey)"k"}{(RedisValue)"v"}");

        // identical bytes: the space is a literal segment, not an argument
        Assert.True(spaced.Span.SequenceEqual(tight.Span));
        Assert.Equal(3, spaced.ArgCount);
        Assert.Equal(new[] { "SET", "k", "v" }, Parse(spaced.Span));
        Assert.Equal(new[] { "k" }, Keys(spaced));
    }

#pragma warning disable SER309 // deliberately exercising the discard path the analyzer exists to prevent
    [Fact]
    public void LiteralsBecomeArgumentsRatherThanBeingDiscarded()
    {
        // literals used to be dropped and the analyzer rejected them outright; now they are tokenized, so
        // the readable spelling works and the analyzer only warns that it resolves per call
        var ctx = new RespContext();

        using var twoSpaces = ctx.Render($"{RedisCommand.GET}  {(RedisKey)"k"}");
        using var hyphen = ctx.Render($"{RedisCommand.GET}-{(RedisKey)"k"}");

        // whitespace-only is still nothing; anything else is now an argument
        Assert.Equal(new[] { "GET", "k" }, Parse(twoSpaces.Span));
        Assert.Equal(2, twoSpaces.ArgCount);

        Assert.Equal(new[] { "GET", "-", "k" }, Parse(hyphen.Span));
        Assert.Equal(3, hyphen.ArgCount);
    }

    [Fact]
    public void ALiteralCommandNowSuppliesTheCommand()
    {
        // this used to throw: "SET " was discarded, so nothing supplied a command and the key could not be
        // framed. The leading token is now the command, so it renders exactly like the hole form.
        var ctx = new RespContext();
        using var frame = ctx.Render($"SET {(RedisKey)"k"}");
        Assert.Equal(new[] { "SET", "k" }, Parse(frame.Span));
    }
#pragma warning restore SER309
}
