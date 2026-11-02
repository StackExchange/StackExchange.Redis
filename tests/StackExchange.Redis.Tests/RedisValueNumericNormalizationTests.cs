using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.Text;
using System.Threading;
using Xunit;

namespace StackExchange.Redis.Tests;

public class RedisValueNumericNormalizationTests
{
    [Theory]
    [InlineData("1,000", "1000")]
    [InlineData("(5)", "-5")]
    [InlineData(" 5 ", "5")]
    [InlineData("1,0,0,0", "1000")]
    [InlineData("5-", "-5")]
    [InlineData("\u00a45", "5")]
    [InlineData("5\0", "5")]
    public void LooseNumericTextRetainsItsTextIdentity(string text, string number)
    {
        CheckStorageForms(text);
        Assert.NotEqual((RedisValue)text, (RedisValue)number);
        Assert.Equal(text.GetHashCode(), ((RedisValue)text).GetHashCode());
        var table = new Hashtable { [(RedisValue)text] = "found" };
        Assert.Equal("found", table[text]);
    }

    [Theory]
    [InlineData("5.", "5")]
    [InlineData("+5", "5")]
    [InlineData("1e2", "100")]
    [InlineData(".5", "0.5")]
    [InlineData("-0.0", "0")]
    [InlineData("18446744073709551615", "18446744073709551615")]
    [InlineData("-9223372036854775808", "-9223372036854775808")]
    public void NumericControls(string text, string canonical)
    {
        CheckStorageForms(text);
        RedisValue x = text, y = canonical;
        Assert.Equal(x, y);
        Assert.Equal(x.GetHashCode(), y.GetHashCode());
        if (text != canonical) Assert.False(RedisValue.EqualityComparer.Binary.Equals(x, y));
    }

    [Fact]
    public void LengthAndUnicodeControls()
    {
        foreach (int length in new[] { 7, 8, 9, Format.MaxInt64TextLen, Format.MaxDoubleTextLen - 1, Format.MaxDoubleTextLen, Format.MaxDoubleTextLen + 1, 127, 128, 129, 512, 4096 })
        {
            foreach (string text in new[] { new string('0', length - 1) + "5", "5." + new string('0', length), new string('1', length), new string('\u4e00', length), "5" + new string('\u4e00', length), new string('x', length) })
                CheckStorageForms(text, allSplits: length < 130);
        }
        foreach (string text in new[] { "", "NaN", "nan", "+NaN", "-NaN", "inf", "INF", "+inf", "-inf", "Infinity", "+Infinity", "1e9999", "1e-9999", "hello", "\u0665", "5\u00a0", "\t5", "5\r\n", "5🙂", "\uFFFD" })
            CheckStorageForms(text);
        Assert.NotEqual(RedisValue.Null, RedisValue.EmptyString);
        Assert.Equal(RedisValue.Null, (RedisValue)(string?)null);
        Assert.Equal(RedisValue.Null, (RedisValue)(byte[]?)null);
        Assert.NotEqual((RedisValue)"nan", (RedisValue)"NaN");
        Assert.NotEqual((RedisValue)"inf", (RedisValue)"INF");
    }

    [Fact]
    public void MalformedUtf8AndSurrogatesKeepDecodedTextSemantics()
    {
        foreach (byte[] bytes in new[] { new byte[] { 0xff }, new byte[] { 0x35, 0xc0, 0xaf }, new byte[] { 0xe2, 0x82 }, new byte[] { 0xed, 0xa0, 0x80 } })
        {
            RedisValue text = Encoding.UTF8.GetString(bytes), blob = bytes;
            Assert.Equal(text, blob);
            Assert.Equal(text.GetHashCode(), blob.GetHashCode());
            Assert.False(RedisValue.EqualityComparer.Binary.Equals(text, blob));
            for (int split = 0; split <= bytes.Length; split++)
            {
                RedisValue sequence = FragmentedSegment<byte>.Create(bytes.AsMemory(0, split), ReadOnlyMemory<byte>.Empty, bytes.AsMemory(split));
                Assert.Equal(text, sequence);
                Assert.Equal(text.GetHashCode(), sequence.GetHashCode());
            }
        }
        foreach (string text in new[] { new string('\uD800', 1), "5" + new string('\uDC00', 1) })
        {
            RedisValue asString = text, asBytes = Encoding.UTF8.GetBytes(text);
            Assert.NotEqual(asString, asBytes);
            Assert.True(RedisValue.EqualityComparer.Binary.Equals(asString, asBytes));
        }
    }

    [Theory]
    [InlineData("1,000", 1000)]
    [InlineData("(5)", -5)]
    [InlineData(" 5 ", 5)]
    [InlineData("1,0,0,0", 1000)]
    [InlineData("5.", 5)]
    [InlineData("+5", 5)]
    [InlineData("1e2", 100)]
    public void ExistingStringConversionsRemainPermissive(string text, int expected)
    {
        RedisValue value = text;
        Assert.Equal((double)expected, (double)value);
        Assert.Equal((decimal)expected, (decimal)value);
        Assert.Equal((float)expected, (float)value);
        Assert.Equal((long)expected, (long)value);
        Assert.Equal((double?)expected, (double?)value);
        Assert.Equal((decimal?)expected, (decimal?)value);
        Assert.Equal((float?)expected, (float?)value);
        var convertible = (IConvertible)value;
        Assert.Equal((double)expected, convertible.ToDouble(CultureInfo.InvariantCulture));
        Assert.Equal((decimal)expected, convertible.ToDecimal(CultureInfo.InvariantCulture));
        Assert.Equal((float)expected, convertible.ToSingle(CultureInfo.InvariantCulture));
        Assert.Equal((double)expected, convertible.ToType(typeof(double), CultureInfo.InvariantCulture));
        Assert.True(value.TryParse(out double parsed));
        Assert.Equal((double)expected, parsed);
    }

    [Fact]
    public void ExistingSpecialConversionFailuresRemain()
    {
        foreach (string text in new[] { "NaN", "inf", "-inf", "Infinity" })
        {
            RedisValue value = text;
            Assert.True(double.IsNaN((double)value) || double.IsInfinity((double)value));
            Assert.Throws<InvalidCastException>(() => (decimal)value);
            Assert.Throws<InvalidCastException>(() => (float)value);
        }
        foreach (string text in new[] { "1,000", "(5)", " 5 ", "1,0,0,0" })
        {
            RedisValue bytes = Encoding.UTF8.GetBytes(text);
            Assert.Throws<InvalidCastException>(() => (double)bytes);
            Assert.Throws<InvalidCastException>(() => (decimal)bytes);
            Assert.Throws<InvalidCastException>(() => (float)bytes);
        }
    }

#if NET
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LongInvalidNumericPrefixDoesNotRentBuffer(bool segmented)
    {
        const int length = 65536;
        string text = "5x" + new string('x', length - 2);
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        RedisValue value = segmented
            ? FragmentedSegment<byte>.Create(ReadOnlyMemory<byte>.Empty, bytes.AsMemory(0, 1), ReadOnlyMemory<byte>.Empty, bytes.AsMemory(1))
            : (RedisValue)text;
        RedisValue other = "no";
        using var listener = new BufferRentListener(length);
        // Verify the observer even when a previous test has warmed the pool bucket.
        byte[] control = ArrayPool<byte>.Shared.Rent(length);
        ArrayPool<byte>.Shared.Return(control);
        Assert.True(listener.Rents > 0);
        listener.Rents = 0;
        bool equal = value == other;
        int rents = listener.Rents;
        Assert.False(equal);
        Assert.Equal(0, rents);
    }

    private sealed class BufferRentListener(int minimumLength) : EventListener
    {
        private readonly int _threadId = Thread.CurrentThread.ManagedThreadId;
        public int Rents { get; set; }

        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (eventSource.Name == "System.Buffers.ArrayPoolEventSource")
                EnableEvents(eventSource, EventLevel.Verbose);
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            if (Thread.CurrentThread.ManagedThreadId == _threadId
                && eventData.EventName == "BufferRented"
                && eventData.Payload?[1] is int length && length >= minimumLength)
                Rents++;
        }
    }
#endif

    [Fact]
    public void LongNumericCandidatesKeepByteParserSemantics()
    {
        foreach (string text in new[] { new string('0', 65536) + "5", "5." + new string('0', 65536), "+5e+00000", "5E-00000" })
        {
            CheckStorageForms(text, allSplits: false);
            Assert.Equal((RedisValue)5, (RedisValue)text);
        }
        foreach (char c in new[] { 'x', ',', ' ', '\0', '\t', '\u00a0', '\u4e00' })
        {
            CheckStorageForms("5" + c + new string('0', 512), allSplits: false);
            CheckStorageForms(new string('0', 512) + "5" + c, allSplits: false);
        }
    }

    private static void CheckStorageForms(string text, bool allSplits = true)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        using var manager = new TestMemoryManager(bytes);
        var forms = new List<RedisValue> { text, bytes, (ReadOnlyMemory<byte>)bytes.AsMemory(), (ReadOnlyMemory<byte>)manager.Memory, RedisValue.FromRaw(bytes) };
        if (allSplits)
        {
            for (int split = 0; split <= bytes.Length; split++)
                forms.Add(FragmentedSegment<byte>.Create(bytes.AsMemory(0, split), ReadOnlyMemory<byte>.Empty, bytes.AsMemory(split)));
        }
        else
        {
            forms.Add(FragmentedSegment<byte>.Create(bytes.AsMemory(0, 1), bytes.AsMemory(1, bytes.Length / 2), bytes.AsMemory(1 + bytes.Length / 2)));
        }
        RedisValue expected = text;
        foreach (RedisValue value in forms)
        {
            Assert.True(expected == value, $"String vs {value.Type}; chars={text.Length}");
            Assert.True(value == expected);
            Assert.Equal(0, expected.CompareTo(value));
            Assert.Equal(0, value.CompareTo(expected));
            Assert.Equal(expected.GetHashCode(), value.GetHashCode());
            Assert.True(RedisValue.EqualityComparer.Default.Equals(expected, value));
            Assert.Equal(RedisValue.EqualityComparer.Default.GetHashCode(expected), RedisValue.EqualityComparer.Default.GetHashCode(value));
            Assert.True(RedisValue.EqualityComparer.Binary.Equals(expected, value));
            Assert.Equal(RedisValue.EqualityComparer.Binary.GetHashCode(expected), RedisValue.EqualityComparer.Binary.GetHashCode(value));
            // Exercise hash-based lookup, rather than an assertion helper's enumeration.
            bool forwardLookup = new HashSet<RedisValue> { expected }.Contains(value);
            bool reverseLookup = new HashSet<RedisValue> { value }.Contains(expected);
            Assert.True(forwardLookup);
            Assert.True(reverseLookup);
            Assert.Equal("found", new Dictionary<RedisValue, string> { [expected] = "found" }[value]);
            Assert.Equal("found", new Dictionary<RedisValue, string> { [value] = "found" }[expected]);
        }
    }

    private sealed class TestMemoryManager(byte[] bytes) : MemoryManager<byte>
    {
        public override Span<byte> GetSpan() => bytes;
        public override MemoryHandle Pin(int elementIndex = 0) => throw new NotSupportedException();
        public override void Unpin() { }
        protected override void Dispose(bool disposing) { }
    }
}
