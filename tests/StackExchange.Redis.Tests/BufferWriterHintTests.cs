using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Writing must not assume <see cref="IBufferWriter{T}.GetSpan"/> honoured the size hint.
/// </summary>
/// <remarks>
/// <para>
/// The hint is a hint. <c>CycleBuffer.GetUncommittedMemory</c> caps it at 1k, sizes fresh segments from the
/// committed total rather than the hint, and - the case with no floor at all - hands back a dangling recycled
/// segment as-is, whatever length that happens to be. So a writer may legitimately return less than asked.
/// </para>
/// <para>
/// These use a writer that returns deliberately short spans, which is what the buffer does intermittently
/// under concurrency. Reported as ArgumentOutOfRangeException from MessageWriter.WriteUnifiedSpan.
/// </para>
/// </remarks>
public class BufferWriterHintTests
{
    /// <summary>
    /// An <see cref="IBufferWriter{T}"/> that honours at most <paramref name="max"/> bytes per span, and
    /// detects writes that run past the span it handed out.
    /// </summary>
    /// <remarks>
    /// The span comes from an isolated scratch array with a canary beyond it, checked on every Advance. A
    /// plain growing buffer cannot catch this: an overrun would land harmlessly in the slack that follows,
    /// and the test would pass while the real writer corrupts whatever is next in the pool.
    /// </remarks>
    private sealed class StingyWriter(int max) : IBufferWriter<byte>
    {
        private const int Canary = 64;
        private const byte CanaryByte = 0xCD;

        /// <summary>A generous but finite ceiling, so the "unconstrained" baseline stays allocatable.</summary>
        private const int Unconstrained = 64 * 1024;

        private readonly int _max = Math.Min(max, Unconstrained);
        private byte[] _scratch = [];
        private int _handedOut;
        private readonly List<byte> _written = [];

        public ReadOnlySpan<byte> Written => _written.ToArray();

        public void Advance(int count)
        {
            if (count < 0 || count > _handedOut)
            {
                throw new InvalidOperationException($"Advance({count}) but only {_handedOut} was handed out");
            }

            for (var i = _handedOut; i < _scratch.Length; i++)
            {
                if (_scratch[i] != CanaryByte)
                {
                    throw new InvalidOperationException(
                        $"buffer overrun: {i - _handedOut + 1} byte(s) written past a span of {_handedOut}");
                }
            }

            for (var i = 0; i < count; i++) _written.Add(_scratch[i]);
            _handedOut = 0;
        }

        public Memory<byte> GetMemory(int sizeHint = 0) => Hand(sizeHint).AsMemory(0, _handedOut);

        public Span<byte> GetSpan(int sizeHint = 0) => Hand(sizeHint).AsSpan(0, _handedOut);

        private byte[] Hand(int sizeHint)
        {
            _handedOut = Math.Max(1, Math.Min(_max, sizeHint <= 0 ? _max : sizeHint));
            _scratch = new byte[_handedOut + Canary];
            _scratch.AsSpan(_handedOut).Fill(CanaryByte);
            return _scratch;
        }
    }

    /// <remarks>
    /// Built directly rather than through <c>TestHarness.Write</c>, which pins the database to -1 and so
    /// cannot issue the key-bearing commands this needs.
    /// </remarks>
    private static string Render(int max, string command, params object[] args)
    {
        var writer = new StingyWriter(max);
        var message = new RedisDatabase.ExecuteMessage(CommandMap.Default, 0, CommandFlags.None, command, args);
        message.WriteTo(new MessageWriter(null, CommandMap.Default, writer));
        return Encoding.UTF8.GetString(writer.Written.ToArray()).Replace("\r\n", "|");
    }

    [Theory]
    // 8 is below every fixed-size hint in the writer; 16 and 64 straddle the length-prefix sites; 520 is
    // just under the 5 + MaxInt32TextLen + 512 that the quick-span path asks for
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(64)]
    [InlineData(520)]
    public void ShortSpansStillProduceCorrectFrames(int max)
    {
        var expected = Render(64 * 1024, "SET", "mykey", "myvalue");
        Assert.Equal("*3|$3|SET|$5|mykey|$7|myvalue|", expected);
        Assert.Equal(expected, Render(max, "SET", "mykey", "myvalue"));
    }

    [Theory]
    [InlineData(8, 500)]
    [InlineData(8, 512)]
    [InlineData(8, 513)]
    [InlineData(520, 512)]   // the reported shape: payloads straddling the 512 boundary
    [InlineData(520, 5000)]
    public void ShortSpansStillProduceCorrectFramesForLargeValues(int max, int payload)
    {
        var value = new string('x', payload);
        var expected = Render(64 * 1024, "SET", "mykey", value);
        Assert.Equal(expected, Render(max, "SET", "mykey", value));
    }

    [Theory]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(40)]
    public void ShortSpansStillProduceCorrectFramesForEveryValueShape(int max)
    {
        // one of each writer path: int64, uint64-range, double, byte[], empty, and a long string
        object[] args =
        [
            "mykey",
            1234567890,
            long.MinValue,
            ulong.MaxValue,
            1.5d,
            new byte[] { 1, 2, 3, 250 },
            "",
            new string('x', 600),
        ];

        Assert.Equal(Render(64 * 1024, "SET", args), Render(max, "SET", args));
    }

    [Fact]
    public void TheBaselineIsItselfCorrect()
    {
        // guards against both sides being equally wrong
        Assert.Equal("*4|$3|SET|$5|mykey|$3|1.5|$2|-1|", Render(64 * 1024, "SET", "mykey", 1.5d, -1));
    }
}
