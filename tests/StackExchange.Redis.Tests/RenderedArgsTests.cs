using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// The codec only: entries round-trip, keys and values stay distinguishable, and the buffer goes back to
/// the pool exactly once however many times it is asked to.
/// </summary>
public class RenderedArgsTests
{
    private static List<(bool IsKey, string Payload)> Drain(in RenderedArgs args)
    {
        var result = new List<(bool, string)>();
        var iter = args.GetEnumerator();
        while (iter.MoveNext())
        {
            result.Add((iter.IsKey, Encoding.UTF8.GetString(iter.Current)));
        }
        return result;
    }

    [Fact]
    public void MixedKeysAndValuesRoundTrip()
    {
        RedisKeyOrValue[] input = [(RedisKey)"k1", (RedisValue)"v1", (RedisKey)"k2", (RedisValue)123];
        var args = RenderedArgs.Create(input, pool: null);
        try
        {
            Assert.Equal(4, args.Count);
            Assert.Equal(
                [(true, "k1"), (false, "v1"), (true, "k2"), (false, "123")],
                Drain(in args));
        }
        finally
        {
            RenderedArgs.Recycle(ref args);
        }
    }

    [Fact]
    public void ScriptFormPutsKeysFirst()
    {
        RedisKey[] keys = ["a", "bb"];
        RedisValue[] values = ["x", "yy", "zzz"];
        var args = RenderedArgs.Create(keys, values, pool: null);
        try
        {
            Assert.Equal(5, args.Count);
            Assert.Equal(
                [(true, "a"), (true, "bb"), (false, "x"), (false, "yy"), (false, "zzz")],
                Drain(in args));
        }
        finally
        {
            RenderedArgs.Recycle(ref args);
        }
    }

    [Fact]
    public void ZeroLengthKeyAndValueStayDistinguishable()
    {
        // this is why the prefix uses ~length rather than -length: negation cannot tell a zero-length
        // key from a zero-length value, because -0 == 0
        RedisKeyOrValue[] input = [(RedisKey)"", (RedisValue)""];
        var args = RenderedArgs.Create(input, pool: null);
        try
        {
            Assert.Equal([(true, ""), (false, "")], Drain(in args));
        }
        finally
        {
            RenderedArgs.Recycle(ref args);
        }
    }

    [Fact]
    public void EmptyInputRendersNothingAndRecyclesCleanly()
    {
        var args = RenderedArgs.Create([], pool: null);
        Assert.Equal(0, args.Count);
        Assert.Empty(Drain(in args));
        RenderedArgs.Recycle(ref args);
        RenderedArgs.Recycle(ref args);
    }

    [Fact]
    public void DefaultInstanceIsSafeToRecycle()
    {
        // a message that fails before its arguments are ever rendered still has to be tidied up
        RenderedArgs args = default;
        Assert.Equal(0, args.Count);
        RenderedArgs.Recycle(ref args);
    }

    [Fact]
    public void RecycleIsOnceOnly()
    {
        var pool = new CountingPool();
        var args = RenderedArgs.Create([(RedisValue)"payload"], pool);
        Assert.Equal(1, pool.Rented);

        RenderedArgs.Recycle(ref args);
        RenderedArgs.Recycle(ref args);
        RenderedArgs.Recycle(ref args);

        Assert.Equal(1, pool.Returned); // and not three

        // Count is deliberately retained: it is what WriteTo checks itself against, so zeroing it here
        // would make a write after recycling look consistent while emitting nothing
        Assert.Equal(1, args.Count);
        Assert.Empty(Drain(in args)); // walking after recycling is empty rather than a fault
    }

    [Fact]
    public void WritingAfterRecyclingFailsLoudlyRatherThanSilently()
    {
        // the header has already declared Count arguments by the time WriteTo runs, so quietly writing
        // fewer would put a malformed frame on the wire and the server would complain later, somewhere
        // else - this is the shape of a use-after-recycle, and it should fail here instead
        var args = RenderedArgs.Create([(RedisKey)"key", (RedisValue)"value"], pool: null);
        RenderedArgs.Recycle(ref args);

        var ex = Assert.Throws<InvalidOperationException>(() => Write(in args));
        Assert.Contains("2 value(s), but wrote 0", ex.Message);
    }

    [Fact]
    public void ConfiguredPoolIsUsedInsteadOfTheSharedArrayPool()
    {
        var pool = new CountingPool();
        var args = RenderedArgs.Create([(RedisKey)"key", (RedisValue)"value"], pool);
        try
        {
            Assert.Equal(1, pool.Rented);
            Assert.Equal([(true, "key"), (false, "value")], Drain(in args));
        }
        finally
        {
            RenderedArgs.Recycle(ref args);
        }
        Assert.Equal(1, pool.Returned);
    }

    [Fact]
    public void LargePayloadsSurviveTheLengthPrefix()
    {
        var big = new string('x', 100_000);
        var args = RenderedArgs.Create([(RedisValue)big], pool: null);
        try
        {
            var drained = Drain(in args);
            Assert.Single(drained);
            Assert.Equal(big, drained[0].Payload);
        }
        finally
        {
            RenderedArgs.Recycle(ref args);
        }
    }

    private static byte[] Write(in RenderedArgs args)
    {
        var writer = new MessageWriter(null, CommandMap.Default, MessageWriter.BlockBuffer);
        ReadOnlyMemory<byte> payload = default;
        try
        {
            args.WriteTo(writer);
            payload = MessageWriter.FlushBlockBuffer();
            return payload.Span.ToArray();
        }
        catch
        {
            MessageWriter.RevertBlockBuffer();
            throw;
        }
        finally
        {
            MessageWriter.ReleaseBlockBuffer(payload);
        }
    }

    [Fact]
    public void WritesEachEntryAsABulkString()
    {
        var args = RenderedArgs.Create([(RedisKey)"key", (RedisValue)"value", (RedisValue)42], pool: null);
        try
        {
            // keys and values are indistinguishable on the wire; the flag is only for routing
            Assert.Equal("$3\r\nkey\r\n$5\r\nvalue\r\n$2\r\n42\r\n", Encoding.UTF8.GetString(Write(in args)));
        }
        finally
        {
            RenderedArgs.Recycle(ref args);
        }
    }

    [Fact]
    public void WritesNothingForNoArguments()
    {
        var args = RenderedArgs.Create([], pool: null);
        try
        {
            Assert.Empty(Write(in args));
        }
        finally
        {
            RenderedArgs.Recycle(ref args);
        }
    }

    [Theory]
    [InlineData("abc")]                 // single key
    [InlineData("abc,abc")]             // same key twice - one slot
    [InlineData("{tag}a,{tag}b")]       // hash tags - still one slot
    [InlineData("one,two")]             // disagreeing keys - MultipleSlots
    [InlineData("")]                    // no keys at all
    public void HashSlotAgreesWithThePerKeyPath(string keyNames)
    {
        var strategy = new ServerSelectionStrategy(null) { ServerType = ServerType.Cluster };
        var keys = keyNames.Length == 0
            ? []
            : keyNames.Split(',').Select(x => (RedisKey)x).ToArray();

        // what the existing per-key routing would decide
        var expected = ServerSelectionStrategy.NoSlot;
        foreach (var key in keys) expected = strategy.CombineSlot(expected, key);

        var args = RenderedArgs.Create(keys, [(RedisValue)"ignored", (RedisValue)"also ignored"], pool: null);
        try
        {
            Assert.Equal(expected, args.GetHashSlot(strategy));
        }
        finally
        {
            RenderedArgs.Recycle(ref args);
        }
    }

    [Fact]
    public void HashSlotIgnoresValuesEvenWhenTheyLookLikeKeys()
    {
        var strategy = new ServerSelectionStrategy(null) { ServerType = ServerType.Cluster };

        var keyed = RenderedArgs.Create([(RedisKey)"abc"], pool: null);
        var valued = RenderedArgs.Create([(RedisValue)"abc"], pool: null);
        try
        {
            Assert.Equal(strategy.HashSlot((RedisKey)"abc"), keyed.GetHashSlot(strategy));
            Assert.Equal(ServerSelectionStrategy.NoSlot, valued.GetHashSlot(strategy));
        }
        finally
        {
            RenderedArgs.Recycle(ref keyed);
            RenderedArgs.Recycle(ref valued);
        }
    }

    [Fact]
    public void HashSlotIsNoSlotForStandalone()
    {
        var strategy = new ServerSelectionStrategy(null) { ServerType = ServerType.Standalone };
        var args = RenderedArgs.Create([(RedisKey)"abc"], pool: null);
        try
        {
            Assert.Equal(ServerSelectionStrategy.NoSlot, args.GetHashSlot(strategy));
        }
        finally
        {
            RenderedArgs.Recycle(ref args);
        }
    }

    private sealed class CountingPool : MemoryPool<byte>
    {
        public int Rented { get; private set; }
        public int Returned { get; private set; }
        public override int MaxBufferSize => Shared.MaxBufferSize;
        public override IMemoryOwner<byte> Rent(int minBufferSize = -1)
        {
            Rented++;
            return new Owner(this, Shared.Rent(minBufferSize));
        }
        protected override void Dispose(bool disposing) { }

        private sealed class Owner(CountingPool pool, IMemoryOwner<byte> inner) : IMemoryOwner<byte>
        {
            public Memory<byte> Memory => inner.Memory;
            public void Dispose() { pool.Returned++; inner.Dispose(); }
        }
    }
}