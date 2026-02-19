using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using RESPite.Internal;
using RESPite.Messages;
using Xunit;
using Xunit.Sdk;
using Xunit.v3;

namespace RESPite.Tests;

public class RespReaderTests(ITestOutputHelper logger)
{
    public readonly struct RespPayload(string label, ReadOnlySequence<byte> payload, byte[] expected, bool? outOfBand, int count)
    {
        public override string ToString() => Label;
        public string Label { get; } = label;
        public ReadOnlySequence<byte> PayloadRaw { get; } = payload;
        public int Length { get; } = CheckPayload(payload, expected, outOfBand, count);
        private static int CheckPayload(scoped in ReadOnlySequence<byte> actual, byte[] expected, bool? outOfBand, int count)
        {
            Assert.Equal(expected.LongLength, actual.Length);
            var pool = ArrayPool<byte>.Shared.Rent(expected.Length);
            actual.CopyTo(pool);
            bool isSame = pool.AsSpan(0, expected.Length).SequenceEqual(expected);
            ArrayPool<byte>.Shared.Return(pool);
            Assert.True(isSame, "Data mismatch");

            // verify that the data exactly passes frame-scanning
            long totalBytes = 0;
            RespReader reader = new(actual);
            while (count > 0)
            {
                RespScanState state = default;
                Assert.True(state.TryRead(ref reader, out long bytesRead));
                totalBytes += bytesRead;
                Assert.True(state.IsComplete, nameof(state.IsComplete));
                if (outOfBand.HasValue)
                {
                    if (outOfBand.Value)
                    {
                        Assert.Equal(RespPrefix.Push, state.Prefix);
                    }
                    else
                    {
                        Assert.NotEqual(RespPrefix.Push, state.Prefix);
                    }
                }
                count--;
            }
            Assert.Equal(expected.Length, totalBytes);
            reader.DemandEnd();
            return expected.Length;
        }

        public RespReader Reader() => new(PayloadRaw);
    }

    public sealed class RespAttribute : DataAttribute
    {
        public override bool SupportsDiscoveryEnumeration() => true;

        private readonly object _value;
        public bool OutOfBand { get; set; } = false;

        private bool? EffectiveOutOfBand => Count == 1 ? OutOfBand : default(bool?);
        public int Count { get; set; } = 1;

        public RespAttribute(string value) => _value = value;
        public RespAttribute(params string[] values) => _value = values;

        public override ValueTask<IReadOnlyCollection<ITheoryDataRow>> GetData(MethodInfo testMethod, DisposalTracker disposalTracker)
            => new(GetData(testMethod).ToArray());

        public IEnumerable<ITheoryDataRow> GetData(MethodInfo testMethod)
        {
            switch (_value)
            {
                case string s:
                    foreach (var item in GetVariants(s, EffectiveOutOfBand, Count))
                    {
                        yield return new TheoryDataRow<RespPayload>(item);
                    }
                    break;
                case string[] arr:
                    foreach (string s in arr)
                    {
                        foreach (var item in GetVariants(s, EffectiveOutOfBand, Count))
                        {
                            yield return new TheoryDataRow<RespPayload>(item);
                        }
                    }
                    break;
            }
        }

        private static IEnumerable<RespPayload> GetVariants(string value, bool? outOfBand, int count)
        {
            var bytes = Encoding.UTF8.GetBytes(value);

            // all in one
            yield return new("Right-sized", new(bytes), bytes, outOfBand, count);

            var bigger = new byte[bytes.Length + 4];
            bytes.CopyTo(bigger.AsSpan(2, bytes.Length));
            bigger.AsSpan(0, 2).Fill(0xFF);
            bigger.AsSpan(bytes.Length + 2, 2).Fill(0xFF);

            // all in one, oversized
            yield return new("Oversized", new(bigger, 2, bytes.Length), bytes, outOfBand, count);

            // two-chunks
            for (int i = 0; i <= bytes.Length; i++)
            {
                int offset = 2 + i;
                var left = new Segment(new ReadOnlyMemory<byte>(bigger, 0, offset), null);
                var right = new Segment(new ReadOnlyMemory<byte>(bigger, offset, bigger.Length - offset), left);
                yield return new($"Split:{i}", new ReadOnlySequence<byte>(left, 2, right, right.Length - 2), bytes, outOfBand, count);
            }

            // N-chunks
            Segment head = new(new(bytes, 0, 1), null), tail = head;
            for (int i = 1; i < bytes.Length; i++)
            {
                tail = new(new(bytes, i, 1), tail);
            }
            yield return new("Chunk-per-byte", new(head, 0, tail, 1), bytes, outOfBand, count);
        }
    }

    [Theory, Resp("$3\r\n128\r\n")]
    public void HandleSplitTokens(RespPayload payload)
    {
        RespReader reader = payload.Reader();
        RespScanState scan = default;
        bool readResult = scan.TryRead(ref reader, out _);
        logger.WriteLine(scan.ToString());
        Assert.Equal(payload.Length, reader.BytesConsumed);
        Assert.True(readResult);
    }

    // the examples from https://github.com/redis/redis-specifications/blob/master/protocol/RESP3.md
    [Theory, Resp("$11\r\nhello world\r\n", "$?\r\n;6\r\nhello \r\n;5\r\nworld\r\n;0\r\n")]
    public void BlobString(RespPayload payload)
    {
        var reader = payload.Reader();
        reader.MoveNext(RespPrefix.BulkString);
        Assert.True(reader.Is("hello world"u8));
        Assert.Equal("hello world", reader.ReadString());
        Assert.Equal("hello world", reader.ReadString(out var prefix));
        Assert.Equal("", prefix);
#if NET7_0_OR_GREATER
        Assert.Equal("hello world", reader.ParseChars<string>());
#endif
        /* interestingly, string does not implement IUtf8SpanParsable
#if NET8_0_OR_GREATER
        Assert.Equal("hello world", reader.ParseBytes<string>());
#endif
        */
        reader.DemandEnd();
    }

    [Theory, Resp("$0\r\n\r\n", "$?\r\n;0\r\n")]
    public void EmptyBlobString(RespPayload payload)
    {
        var reader = payload.Reader();
        reader.MoveNext(RespPrefix.BulkString);
        Assert.True(reader.Is(""u8));
        Assert.Equal("", reader.ReadString());
        reader.DemandEnd();
    }

    [Theory, Resp("+hello world\r\n")]
    public void SimpleString(RespPayload payload)
    {
        var reader = payload.Reader();
        reader.MoveNext(RespPrefix.SimpleString);
        Assert.True(reader.Is("hello world"u8));
        Assert.Equal("hello world", reader.ReadString());
        Assert.Equal("hello world", reader.ReadString(out var prefix));
        Assert.Equal("", prefix);
        reader.DemandEnd();
    }

    [Theory, Resp("-ERR this is the error description\r\n")]
    public void SimpleError_ImplicitErrors(RespPayload payload)
    {
        var ex = Assert.Throws<RespException>(() =>
        {
            var reader = payload.Reader();
            reader.MoveNext();
        });
        Assert.Equal("ERR this is the error description", ex.Message);
    }

    [Theory, Resp("-ERR this is the error description\r\n")]
    public void SimpleError_Careful(RespPayload payload)
    {
        var reader = payload.Reader();
        Assert.True(reader.TryMoveNext(checkError: false));
        Assert.Equal(RespPrefix.SimpleError, reader.Prefix);
        Assert.True(reader.Is("ERR this is the error description"u8));
        Assert.Equal("ERR this is the error description", reader.ReadString());
        reader.DemandEnd();
    }

    [Theory, Resp(":1234\r\n")]
    public void Number(RespPayload payload)
    {
        var reader = payload.Reader();
        reader.MoveNext(RespPrefix.Integer);
        Assert.True(reader.Is("1234"u8));
        Assert.Equal("1234", reader.ReadString());
        Assert.Equal(1234, reader.ReadInt32());
        Assert.Equal(1234D, reader.ReadDouble());
        Assert.Equal(1234M, reader.ReadDecimal());
#if NET7_0_OR_GREATER
        Assert.Equal(1234, reader.ParseChars<int>());
        Assert.Equal(1234D, reader.ParseChars<double>());
        Assert.Equal(1234M, reader.ParseChars<decimal>());
#endif
#if NET8_0_OR_GREATER
        Assert.Equal(1234, reader.ParseBytes<int>());
        Assert.Equal(1234D, reader.ParseBytes<double>());
        Assert.Equal(1234M, reader.ParseBytes<decimal>());
#endif
        reader.DemandEnd();
    }

    [Theory, Resp("_\r\n")]
    public void Null(RespPayload payload)
    {
        var reader = payload.Reader();
        reader.MoveNext(RespPrefix.Null);
        Assert.True(reader.Is(""u8));
        Assert.Null(reader.ReadString());
        reader.DemandEnd();
    }

    [Theory, Resp("$-1\r\n")]
    public void NullString(RespPayload payload)
    {
        var reader = payload.Reader();
        reader.MoveNext(RespPrefix.BulkString);
        Assert.True(reader.IsNull);
        Assert.Null(reader.ReadString());
        Assert.Equal(0, reader.ScalarLength());
        Assert.True(reader.Is(""u8));
        Assert.True(reader.ScalarIsEmpty());

        var iterator = reader.ScalarChunks();
        Assert.False(iterator.MoveNext());
        iterator.MovePast(out reader);
        reader.DemandEnd();
    }

    [Theory, Resp(",1.23\r\n")]
    public void Double(RespPayload payload)
    {
        var reader = payload.Reader();
        reader.MoveNext(RespPrefix.Double);
        Assert.True(reader.Is("1.23"u8));
        Assert.Equal("1.23", reader.ReadString());
        Assert.Equal(1.23D, reader.ReadDouble());
        Assert.Equal(1.23M, reader.ReadDecimal());
        reader.DemandEnd();
    }

    [Theory, Resp(":10\r\n")]
    public void Integer_Simple(RespPayload payload)
    {
        var reader = payload.Reader();
        reader.MoveNext(RespPrefix.Integer);
        Assert.True(reader.Is("10"u8));
        Assert.Equal("10", reader.ReadString());
        Assert.Equal(10, reader.ReadInt32());
        Assert.Equal(10D, reader.ReadDouble());
        Assert.Equal(10M, reader.ReadDecimal());
        reader.DemandEnd();
    }

    [Theory, Resp(",10\r\n")]
    public void Double_Simple(RespPayload payload)
    {
        var reader = payload.Reader();
        reader.MoveNext(RespPrefix.Double);
        Assert.True(reader.Is("10"u8));
        Assert.Equal("10", reader.ReadString());
        Assert.Equal(10, reader.ReadInt32());
        Assert.Equal(10D, reader.ReadDouble());
        Assert.Equal(10M, reader.ReadDecimal());
        reader.DemandEnd();
    }

    [Theory, Resp(",inf\r\n")]
    public void Double_Infinity(RespPayload payload)
    {
        var reader = payload.Reader();
        reader.MoveNext(RespPrefix.Double);
        Assert.True(reader.Is("inf"u8));
        Assert.Equal("inf", reader.ReadString());
        var val = reader.ReadDouble();
        Assert.True(double.IsInfinity(val));
        Assert.True(double.IsPositiveInfinity(val));
        reader.DemandEnd();
    }

    [Theory, Resp(",+inf\r\n")]
    public void Double_PosInfinity(RespPayload payload)
    {
        var reader = payload.Reader();
        reader.MoveNext(RespPrefix.Double);
        Assert.True(reader.Is("+inf"u8));
        Assert.Equal("+inf", reader.ReadString());
        var val = reader.ReadDouble();
        Assert.True(double.IsInfinity(val));
        Assert.True(double.IsPositiveInfinity(val));
        reader.DemandEnd();
    }

    [Theory, Resp(",-inf\r\n")]
    public void Double_NegInfinity(RespPayload payload)
    {
        var reader = payload.Reader();
        reader.MoveNext(RespPrefix.Double);
        Assert.True(reader.Is("-inf"u8));
        Assert.Equal("-inf", reader.ReadString());
        var val = reader.ReadDouble();
        Assert.True(double.IsInfinity(val));
        Assert.True(double.IsNegativeInfinity(val));
        reader.DemandEnd();
    }

    [Theory, Resp(",nan\r\n")]
    public void Double_NaN(RespPayload payload)
    {
        var reader = payload.Reader();
        reader.MoveNext(RespPrefix.Double);
        Assert.True(reader.Is("nan"u8));
        Assert.Equal("nan", reader.ReadString());
        var val = reader.ReadDouble();
        Assert.True(double.IsNaN(val));
        reader.DemandEnd();
    }

    [Theory, Resp("#t\r\n")]
    public void Boolean_T(RespPayload payload)
    {
        var reader = payload.Reader();
        reader.MoveNext(RespPrefix.Boolean);
        Assert.True(reader.ReadBoolean());
        reader.DemandEnd();
    }

    [Theory, Resp("#f\r\n")]
    public void Boolean_F(RespPayload payload)
    {
        var reader = payload.Reader();
        reader.MoveNext(RespPrefix.Boolean);
        Assert.False(reader.ReadBoolean());
        reader.DemandEnd();
    }

    [Theory, Resp(":1\r\n")]
    public void Boolean_1(RespPayload payload)
    {
        var reader = payload.Reader();
        reader.MoveNext(RespPrefix.Integer);
        Assert.True(reader.ReadBoolean());
        reader.DemandEnd();
    }

    [Theory, Resp(":0\r\n")]
    public void Boolean_0(RespPayload payload)
    {
        var reader = payload.Reader();
        reader.MoveNext(RespPrefix.Integer);
        Assert.False(reader.ReadBoolean());
        reader.DemandEnd();
    }

    [Theory, Resp("!21\r\nSYNTAX invalid syntax\r\n", "!?\r\n;6\r\nSYNTAX\r\n;15\r\n invalid syntax\r\n;0\r\n")]
    public void BlobError_ImplicitErrors(RespPayload payload)
    {
        var ex = Assert.Throws<RespException>(() =>
        {
            var reader = payload.Reader();
            reader.MoveNext();
        });
        Assert.Equal("SYNTAX invalid syntax", ex.Message);
    }

    [Theory, Resp("!21\r\nSYNTAX invalid syntax\r\n", "!?\r\n;6\r\nSYNTAX\r\n;15\r\n invalid syntax\r\n;0\r\n")]
    public void BlobError_Careful(RespPayload payload)
    {
        var reader = payload.Reader();
        Assert.True(reader.TryMoveNext(checkError: false));
        Assert.Equal(RespPrefix.BulkError, reader.Prefix);
        Assert.True(reader.Is("SYNTAX invalid syntax"u8));
        Assert.Equal("SYNTAX invalid syntax", reader.ReadString());
        reader.DemandEnd();
    }

    [Theory, Resp("=15\r\ntxt:Some string\r\n", "=?\r\n;4\r\ntxt:\r\n;11\r\nSome string\r\n;0\r\n")]
    public void VerbatimString(RespPayload payload)
    {
        var reader = payload.Reader();
        reader.MoveNext(RespPrefix.VerbatimString);
        Assert.Equal("Some string", reader.ReadString());
        Assert.Equal("Some string", reader.ReadString(out var prefix));
        Assert.Equal("txt", prefix);

        Assert.Equal("Some string", reader.ReadString(out var prefix2));
        Assert.Same(prefix, prefix2); // check prefix recognized and reuse literal
        reader.DemandEnd();
    }

    [Theory, Resp("(3492890328409238509324850943850943825024385\r\n")]
    public void BigIntegers(RespPayload payload)
    {
        var reader = payload.Reader();
        reader.MoveNext(RespPrefix.BigInteger);
        Assert.Equal("3492890328409238509324850943850943825024385", reader.ReadString());
#if NET8_0_OR_GREATER
        var actual = reader.ParseChars(chars => BigInteger.Parse(chars, CultureInfo.InvariantCulture));

        var expected = BigInteger.Parse("3492890328409238509324850943850943825024385");
        Assert.Equal(expected, actual);
#endif
    }

    [Theory, Resp("*3\r\n:1\r\n:2\r\n:3\r\n", "*?\r\n:1\r\n:2\r\n:3\r\n.\r\n")]
    public void Array(RespPayload payload)
    {
        var reader = payload.Reader();
        reader.MoveNext(RespPrefix.Array);
        Assert.Equal(3, reader.AggregateLength());
        var iterator = reader.AggregateChildren();
        Assert.True(iterator.MoveNext(RespPrefix.Integer));
        Assert.Equal(1, iterator.Value.ReadInt32());
        iterator.Value.DemandEnd();

        Assert.True(iterator.MoveNext(RespPrefix.Integer));
        Assert.Equal(2, iterator.Value.ReadInt32());
        iterator.Value.DemandEnd();

        Assert.True(iterator.MoveNext(RespPrefix.Integer));
        Assert.Equal(3, iterator.Value.ReadInt32());
        iterator.Value.DemandEnd();

        Assert.False(iterator.MoveNext(RespPrefix.Integer));
        iterator.MovePast(out reader);
        reader.DemandEnd();

        reader = payload.Reader();
        reader.MoveNext(RespPrefix.Array);
        int[] arr = new int[reader.AggregateLength()];
        int i = 0;
#pragma warning disable SERDBG // warning about .Current vs .Value
        foreach (var sub in reader.AggregateChildren())
#pragma warning restore SERDBG
        {
            sub.MoveNext(RespPrefix.Integer);
            arr[i++] = sub.ReadInt32();
            sub.DemandEnd();
        }
        iterator.MovePast(out reader);
        reader.DemandEnd();

        Assert.Equal([1, 2, 3], arr);
    }

    [Theory, Resp("*-1\r\n")]
    public void NullArray(RespPayload payload)
    {
        var reader = payload.Reader();
        reader.MoveNext(RespPrefix.Array);
        Assert.True(reader.IsNull);
        Assert.Equal(0, reader.AggregateLength());
        var iterator = reader.AggregateChildren();
        Assert.False(iterator.MoveNext());
        iterator.MovePast(out reader);
        reader.DemandEnd();
    }

    [Theory, Resp("*2\r\n*3\r\n:1\r\n$5\r\nhello\r\n:2\r\n#f\r\n", "*?\r\n*?\r\n:1\r\n$5\r\nhello\r\n:2\r\n.\r\n#f\r\n.\r\n")]
    public void NestedArray(RespPayload payload)
    {
        var reader = payload.Reader();
        reader.MoveNext(RespPrefix.Array);

        Assert.Equal(2, reader.AggregateLength());

        var iterator = reader.AggregateChildren();
        Assert.True(iterator.MoveNext(RespPrefix.Array));

        Assert.Equal(3, iterator.Value.AggregateLength());
        var subIterator = iterator.Value.AggregateChildren();
        Assert.True(subIterator.MoveNext(RespPrefix.Integer));
        Assert.Equal(1, subIterator.Value.ReadInt64());
        subIterator.Value.DemandEnd();

        Assert.True(subIterator.MoveNext(RespPrefix.BulkString));
        Assert.True(subIterator.Value.Is("hello"u8));
        subIterator.Value.DemandEnd();

        Assert.True(subIterator.MoveNext(RespPrefix.Integer));
        Assert.Equal(2, subIterator.Value.ReadInt64());
        subIterator.Value.DemandEnd();

        Assert.False(subIterator.MoveNext());

        Assert.True(iterator.MoveNext(RespPrefix.Boolean));
        Assert.False(iterator.Value.ReadBoolean());
        iterator.Value.DemandEnd();

        Assert.False(iterator.MoveNext());
        iterator.MovePast(out reader);

        reader.DemandEnd();
    }

    [Theory, Resp("%2\r\n+first\r\n:1\r\n+second\r\n:2\r\n", "%?\r\n+first\r\n:1\r\n+second\r\n:2\r\n.\r\n")]
    public void Map(RespPayload payload)
    {
        var reader = payload.Reader();
        reader.MoveNext(RespPrefix.Map);

        Assert.Equal(4, reader.AggregateLength());

        var iterator = reader.AggregateChildren();

        Assert.True(iterator.MoveNext(RespPrefix.SimpleString));
        Assert.True(iterator.Value.Is("first".AsSpan()));
        iterator.Value.DemandEnd();

        Assert.True(iterator.MoveNext(RespPrefix.Integer));
        Assert.Equal(1, iterator.Value.ReadInt32());
        iterator.Value.DemandEnd();

        Assert.True(iterator.MoveNext(RespPrefix.SimpleString));
        Assert.True(iterator.Value.Is("second"u8));
        iterator.Value.DemandEnd();

        Assert.True(iterator.MoveNext(RespPrefix.Integer));
        Assert.Equal(2, iterator.Value.ReadInt32());
        iterator.Value.DemandEnd();

        Assert.False(iterator.MoveNext());

        iterator.MovePast(out reader);
        reader.DemandEnd();
    }

    [Theory, Resp("~5\r\n+orange\r\n+apple\r\n#t\r\n:100\r\n:999\r\n", "~?\r\n+orange\r\n+apple\r\n#t\r\n:100\r\n:999\r\n.\r\n")]
    public void Set(RespPayload payload)
    {
        var reader = payload.Reader();
        reader.MoveNext(RespPrefix.Set);

        Assert.Equal(5, reader.AggregateLength());

        var iterator = reader.AggregateChildren();

        Assert.True(iterator.MoveNext(RespPrefix.SimpleString));
        Assert.True(iterator.Value.Is("orange".AsSpan()));
        iterator.Value.DemandEnd();

        Assert.True(iterator.MoveNext(RespPrefix.SimpleString));
        Assert.True(iterator.Value.Is("apple"u8));
        iterator.Value.DemandEnd();

        Assert.True(iterator.MoveNext(RespPrefix.Boolean));
        Assert.True(iterator.Value.ReadBoolean());
        iterator.Value.DemandEnd();

        Assert.True(iterator.MoveNext(RespPrefix.Integer));
        Assert.Equal(100, iterator.Value.ReadInt32());
        iterator.Value.DemandEnd();

        Assert.True(iterator.MoveNext(RespPrefix.Integer));
        Assert.Equal(999, iterator.Value.ReadInt32());
        iterator.Value.DemandEnd();

        Assert.False(iterator.MoveNext());

        iterator.MovePast(out reader);
        reader.DemandEnd();
    }

    private sealed class TestAttributeReader : RespAttributeReader<(int Count, int Ttl, decimal A, decimal B)>
    {
        public override void Read(ref RespReader reader, ref (int Count, int Ttl, decimal A, decimal B) value)
        {
            value.Count += ReadKeyValuePairs(ref reader, ref value);
        }
        private TestAttributeReader() { }
        public static readonly TestAttributeReader Instance = new();
        public static (int Count, int Ttl, decimal A, decimal B) Zero = (0, 0, 0, 0);
        public override bool ReadKeyValuePair(scoped ReadOnlySpan<byte> key, ref RespReader reader, ref (int Count, int Ttl, decimal A, decimal B) value)
        {
            if (key.SequenceEqual("ttl"u8) && reader.IsScalar)
            {
                value.Ttl = reader.ReadInt32();
            }
            else if (key.SequenceEqual("key-popularity"u8) && reader.IsAggregate)
            {
                ReadKeyValuePairs(ref reader, ref value); // recurse to process a/b below
            }
            else if (key.SequenceEqual("a"u8) && reader.IsScalar)
            {
                value.A = reader.ReadDecimal();
            }
            else if (key.SequenceEqual("b"u8) && reader.IsScalar)
            {
                value.B = reader.ReadDecimal();
            }
            else
            {
                return false; // not recognized
            }
            return true; // recognized
        }
    }

    [Theory, Resp(
        "|1\r\n+key-popularity\r\n%2\r\n$1\r\na\r\n,0.1923\r\n$1\r\nb\r\n,0.0012\r\n*2\r\n:2039123\r\n:9543892\r\n",
        "|1\r\n+key-popularity\r\n%2\r\n$1\r\na\r\n,0.1923\r\n$1\r\nb\r\n,0.0012\r\n*?\r\n:2039123\r\n:9543892\r\n.\r\n")]
    public void AttributeRoot(RespPayload payload)
    {
        // ignore the attribute data
        var reader = payload.Reader();
        reader.MoveNext(RespPrefix.Array);
        Assert.Equal(2, reader.AggregateLength());
        var iterator = reader.AggregateChildren();

        Assert.True(iterator.MoveNext(RespPrefix.Integer));
        Assert.Equal(2039123, iterator.Value.ReadInt32());
        iterator.Value.DemandEnd();

        Assert.True(iterator.MoveNext(RespPrefix.Integer));
        Assert.Equal(9543892, iterator.Value.ReadInt32());
        iterator.Value.DemandEnd();

        Assert.False(iterator.MoveNext());
        iterator.MovePast(out reader);
        reader.DemandEnd();

        // process the attribute data
        var state = TestAttributeReader.Zero;
        reader = payload.Reader();
        reader.MoveNext(RespPrefix.Array, TestAttributeReader.Instance, ref state);
        Assert.Equal(1, state.Count);
        Assert.Equal(0.1923M, state.A);
        Assert.Equal(0.0012M, state.B);
        state = TestAttributeReader.Zero;

        Assert.Equal(2, reader.AggregateLength());
        iterator = reader.AggregateChildren();

        Assert.True(iterator.MoveNext(RespPrefix.Integer, TestAttributeReader.Instance, ref state));
        Assert.Equal(2039123, iterator.Value.ReadInt32());
        Assert.Equal(0, state.Count);
        iterator.Value.DemandEnd();

        Assert.True(iterator.MoveNext(RespPrefix.Integer, TestAttributeReader.Instance, ref state));
        Assert.Equal(9543892, iterator.Value.ReadInt32());
        Assert.Equal(0, state.Count);
        iterator.Value.DemandEnd();

        Assert.False(iterator.MoveNext());
        iterator.MovePast(out reader);
        reader.DemandEnd();
    }

    [Theory, Resp("*3\r\n:1\r\n:2\r\n|1\r\n+ttl\r\n:3600\r\n:3\r\n", "*?\r\n:1\r\n:2\r\n|1\r\n+ttl\r\n:3600\r\n:3\r\n.\r\n")]
    public void AttributeInner(RespPayload payload)
    {
        // ignore the attribute data
        var reader = payload.Reader();
        reader.MoveNext(RespPrefix.Array);
        Assert.Equal(3, reader.AggregateLength());
        var iterator = reader.AggregateChildren();

        Assert.True(iterator.MoveNext(RespPrefix.Integer));
        Assert.Equal(1, iterator.Value.ReadInt32());
        iterator.Value.DemandEnd();

        Assert.True(iterator.MoveNext(RespPrefix.Integer));
        Assert.Equal(2, iterator.Value.ReadInt32());
        iterator.Value.DemandEnd();

        Assert.True(iterator.MoveNext(RespPrefix.Integer));
        Assert.Equal(3, iterator.Value.ReadInt32());
        iterator.Value.DemandEnd();

        Assert.False(iterator.MoveNext());
        iterator.MovePast(out reader);
        reader.DemandEnd();

        // process the attribute data
        var state = TestAttributeReader.Zero;
        reader = payload.Reader();
        reader.MoveNext(RespPrefix.Array, TestAttributeReader.Instance, ref state);
        Assert.Equal(0, state.Count);
        Assert.Equal(3, reader.AggregateLength());
        iterator = reader.AggregateChildren();

        Assert.True(iterator.MoveNext(RespPrefix.Integer, TestAttributeReader.Instance, ref state));
        Assert.Equal(0, state.Count);
        Assert.Equal(1, iterator.Value.ReadInt32());
        iterator.Value.DemandEnd();

        Assert.True(iterator.MoveNext(RespPrefix.Integer, TestAttributeReader.Instance, ref state));
        Assert.Equal(0, state.Count);
        Assert.Equal(2, iterator.Value.ReadInt32());
        iterator.Value.DemandEnd();

        Assert.True(iterator.MoveNext(RespPrefix.Integer, TestAttributeReader.Instance, ref state));
        Assert.Equal(1, state.Count);
        Assert.Equal(3600, state.Ttl);
        state = TestAttributeReader.Zero; // reset
        Assert.Equal(3, iterator.Value.ReadInt32());
        iterator.Value.DemandEnd();

        Assert.False(iterator.MoveNextRaw(TestAttributeReader.Instance, ref state));
        Assert.Equal(0, state.Count);
        iterator.MovePast(out reader);
        reader.DemandEnd();
    }

    [Theory, Resp(">3\r\n+message\r\n+somechannel\r\n+this is the message\r\n", OutOfBand = true)]
    public void Push(RespPayload payload)
    {
        // ignore the attribute data
        var reader = payload.Reader();
        reader.MoveNext(RespPrefix.Push);
        Assert.Equal(3, reader.AggregateLength());
        var iterator = reader.AggregateChildren();

        Assert.True(iterator.MoveNext(RespPrefix.SimpleString));
        Assert.True(iterator.Value.Is("message"u8));
        iterator.Value.DemandEnd();

        Assert.True(iterator.MoveNext(RespPrefix.SimpleString));
        Assert.True(iterator.Value.Is("somechannel"u8));
        iterator.Value.DemandEnd();

        Assert.True(iterator.MoveNext(RespPrefix.SimpleString));
        Assert.True(iterator.Value.Is("this is the message"u8));
        iterator.Value.DemandEnd();

        Assert.False(iterator.MoveNext());
        iterator.MovePast(out reader);
        reader.DemandEnd();
    }

    [Theory, Resp(">3\r\n+message\r\n+somechannel\r\n+this is the message\r\n$9\r\nGet-Reply\r\n", Count = 2)]
    public void PushThenGetReply(RespPayload payload)
    {
        // ignore the attribute data
        var reader = payload.Reader();
        reader.MoveNext(RespPrefix.Push);
        Assert.Equal(3, reader.AggregateLength());
        var iterator = reader.AggregateChildren();

        Assert.True(iterator.MoveNext(RespPrefix.SimpleString));
        Assert.True(iterator.Value.Is("message"u8));
        iterator.Value.DemandEnd();

        Assert.True(iterator.MoveNext(RespPrefix.SimpleString));
        Assert.True(iterator.Value.Is("somechannel"u8));
        iterator.Value.DemandEnd();

        Assert.True(iterator.MoveNext(RespPrefix.SimpleString));
        Assert.True(iterator.Value.Is("this is the message"u8));
        iterator.Value.DemandEnd();

        Assert.False(iterator.MoveNext());
        iterator.MovePast(out reader);

        reader.MoveNext(RespPrefix.BulkString);
        Assert.True(reader.Is("Get-Reply"u8));
        reader.DemandEnd();
    }

    [Theory, Resp("$9\r\nGet-Reply\r\n>3\r\n+message\r\n+somechannel\r\n+this is the message\r\n", Count = 2)]
    public void GetReplyThenPush(RespPayload payload)
    {
        // ignore the attribute data
        var reader = payload.Reader();

        reader.MoveNext(RespPrefix.BulkString);
        Assert.True(reader.Is("Get-Reply"u8));

        reader.MoveNext(RespPrefix.Push);
        Assert.Equal(3, reader.AggregateLength());
        var iterator = reader.AggregateChildren();

        Assert.True(iterator.MoveNext(RespPrefix.SimpleString));
        Assert.True(iterator.Value.Is("message"u8));
        iterator.Value.DemandEnd();

        Assert.True(iterator.MoveNext(RespPrefix.SimpleString));
        Assert.True(iterator.Value.Is("somechannel"u8));
        iterator.Value.DemandEnd();

        Assert.True(iterator.MoveNext(RespPrefix.SimpleString));
        Assert.True(iterator.Value.Is("this is the message"u8));
        iterator.Value.DemandEnd();

        Assert.False(iterator.MoveNext());
        iterator.MovePast(out reader);

        reader.DemandEnd();
    }

    [Theory, Resp("*0\r\n$4\r\npass\r\n", "*1\r\n+ok\r\n$4\r\npass\r\n", "*-1\r\n$4\r\npass\r\n", "*?\r\n.\r\n$4\r\npass\r\n", Count = 2)]
    public void ArrayThenString(RespPayload payload)
    {
        var reader = payload.Reader();
        Assert.True(reader.TryMoveNext(RespPrefix.Array));
        reader.SkipChildren();

        Assert.True(reader.TryMoveNext(RespPrefix.BulkString));
        Assert.True(reader.Is("pass"u8));

        reader.DemandEnd();

        // and the same using child iterator
        reader = payload.Reader();
        Assert.True(reader.TryMoveNext(RespPrefix.Array));
        var iterator = reader.AggregateChildren();
        iterator.MovePast(out reader);

        Assert.True(reader.TryMoveNext(RespPrefix.BulkString));
        Assert.True(reader.Is("pass"u8));

        reader.DemandEnd();
    }

    // Tests for ScalarLengthIs
    [Theory, Resp("$-1\r\n")] // null bulk string
    public void ScalarLengthIs_NullBulkString(RespPayload payload)
    {
        var reader = payload.Reader();
        reader.MoveNext(RespPrefix.BulkString);
        Assert.True(reader.ScalarLengthIs(0));
        Assert.False(reader.ScalarLengthIs(1));
        Assert.False(reader.ScalarLengthIs(5));
        reader.DemandEnd();
    }

    // Note: Null prefix (_\r\n) is tested in the existing Null() test above
    [Theory, Resp("$0\r\n\r\n", "$?\r\n;0\r\n")] // empty scalar (simple and streaming)
    public void ScalarLengthIs_Empty(RespPayload payload)
    {
        var reader = payload.Reader();
        reader.MoveNext(RespPrefix.BulkString);
        Assert.True(reader.ScalarLengthIs(0));
        Assert.False(reader.ScalarLengthIs(1));
        Assert.False(reader.ScalarLengthIs(5));
        reader.DemandEnd();
    }

    [Theory, Resp("$5\r\nhello\r\n")] // simple scalar
    public void ScalarLengthIs_Simple(RespPayload payload)
    {
        var reader = payload.Reader();
        reader.MoveNext(RespPrefix.BulkString);
        Assert.True(reader.ScalarLengthIs(5));
        Assert.False(reader.ScalarLengthIs(0));
        Assert.False(reader.ScalarLengthIs(4));
        Assert.False(reader.ScalarLengthIs(6));
        Assert.False(reader.ScalarLengthIs(10));
        reader.DemandEnd();
    }

    [Theory, Resp("$?\r\n;2\r\nhe\r\n;3\r\nllo\r\n;0\r\n")] // streaming scalar
    public void ScalarLengthIs_Streaming(RespPayload payload)
    {
        var reader = payload.Reader();
        reader.MoveNext(RespPrefix.BulkString);
        Assert.True(reader.ScalarLengthIs(5));
        Assert.False(reader.ScalarLengthIs(0));
        Assert.False(reader.ScalarLengthIs(2)); // short-circuit: stops early
        Assert.False(reader.ScalarLengthIs(3)); // short-circuit: stops early
        Assert.False(reader.ScalarLengthIs(6)); // short-circuit: stops early
        Assert.False(reader.ScalarLengthIs(10)); // short-circuit: stops early
        reader.DemandEnd();
    }

    [Fact] // streaming scalar - verify short-circuiting stops before reading malformed data
    public void ScalarLengthIs_Streaming_ShortCircuits()
    {
        // Streaming scalar: 2 bytes "he", then 3 bytes "llo", then 1 byte "X", then MALFORMED
        // To check if length == N, we need to read N+1 bytes to verify there isn't more
        // So malformed data must come AFTER the N+1 threshold
        var data = "$?\r\n;2\r\nhe\r\n;3\r\nllo\r\n;1\r\nX\r\nMALFORMED"u8.ToArray();
        var reader = new RespReader(new ReadOnlySequence<byte>(data));
        reader.MoveNext(RespPrefix.BulkString);

        // When checking length < 6, we read up to 6 bytes (he+llo+X), see 6 > expected, stop
        Assert.False(reader.ScalarLengthIs(0)); // reads "he" (2), 2 > 0, stops before "llo"
        Assert.False(reader.ScalarLengthIs(2)); // reads "he" (2), "llo" (5 total), 5 > 2, stops before "X"
        Assert.False(reader.ScalarLengthIs(4)); // reads "he" (2), "llo" (5 total), 5 > 4, stops before "X"
        Assert.False(reader.ScalarLengthIs(5)); // reads "he" (2), "llo" (5), "X" (6 total), 6 > 5, stops before MALFORMED

        // All of the above should succeed without hitting MALFORMED because we short-circuit
    }

    [Fact] // streaming scalar - verify TryGetSpan fails and Buffer works correctly
    public void StreamingScalar_BufferPartial()
    {
        // 32 bytes total: "abcdefgh" (8) + "ijklmnop" (8) + "qrstuvwx" (8) + "yz012345" (8) + "6789" (4)
        var data = "$?\r\n;8\r\nabcdefgh\r\n;8\r\nijklmnop\r\n;8\r\nqrstuvwx\r\n;8\r\nyz012345\r\n;4\r\n6789\r\n;0\r\n"u8.ToArray();
        var reader = new RespReader(new ReadOnlySequence<byte>(data));
        reader.MoveNext(RespPrefix.BulkString);

        Assert.True(reader.IsScalar);
        Assert.False(reader.TryGetSpan(out _)); // Should fail - data is non-contiguous

        // Buffer should fetch just the first 16 bytes
        Span<byte> buffer = stackalloc byte[16];
        var buffered = reader.Buffer(buffer);
        Assert.Equal(16, buffered.Length);
        Assert.True(buffered.SequenceEqual("abcdefghijklmnop"u8));
    }

    [Theory, Resp("+hello\r\n")] // simple string
    public void ScalarLengthIs_SimpleString(RespPayload payload)
    {
        var reader = payload.Reader();
        reader.MoveNext(RespPrefix.SimpleString);
        Assert.True(reader.ScalarLengthIs(5));
        Assert.False(reader.ScalarLengthIs(0));
        Assert.False(reader.ScalarLengthIs(4));
        reader.DemandEnd();
    }

    // Tests for AggregateLengthIs
    [Theory, Resp("*-1\r\n")] // null array
    public void AggregateLengthIs_NullArray(RespPayload payload)
    {
        var reader = payload.Reader();
        reader.MoveNext(RespPrefix.Array);
        Assert.True(reader.IsNull);
        // Note: AggregateLength() would throw on null, but AggregateLengthIs should handle it
        reader.DemandEnd();
    }

    [Theory, Resp("*0\r\n", "*?\r\n.\r\n")] // empty array (simple and streaming)
    public void AggregateLengthIs_Empty(RespPayload payload)
    {
        var reader = payload.Reader();
        reader.MoveNext(RespPrefix.Array);
        Assert.True(reader.AggregateLengthIs(0));
        Assert.False(reader.AggregateLengthIs(1));
        Assert.False(reader.AggregateLengthIs(3));
        reader.SkipChildren();
        reader.DemandEnd();
    }

    [Theory, Resp("*3\r\n:1\r\n:2\r\n:3\r\n")] // simple array
    public void AggregateLengthIs_Simple(RespPayload payload)
    {
        var reader = payload.Reader();
        reader.MoveNext(RespPrefix.Array);
        Assert.True(reader.AggregateLengthIs(3));
        Assert.False(reader.AggregateLengthIs(0));
        Assert.False(reader.AggregateLengthIs(2));
        Assert.False(reader.AggregateLengthIs(4));
        Assert.False(reader.AggregateLengthIs(10));
        reader.SkipChildren();
        reader.DemandEnd();
    }

    [Theory, Resp("*?\r\n:1\r\n:2\r\n:3\r\n.\r\n")] // streaming array
    public void AggregateLengthIs_Streaming(RespPayload payload)
    {
        var reader = payload.Reader();
        reader.MoveNext(RespPrefix.Array);
        Assert.True(reader.AggregateLengthIs(3));
        Assert.False(reader.AggregateLengthIs(0));
        Assert.False(reader.AggregateLengthIs(2)); // short-circuit: stops early
        Assert.False(reader.AggregateLengthIs(4)); // short-circuit: stops early
        Assert.False(reader.AggregateLengthIs(10)); // short-circuit: stops early
        reader.SkipChildren();
        reader.DemandEnd();
    }

    [Fact] // streaming array - verify short-circuiting works even with extra data present
    public void AggregateLengthIs_Streaming_ShortCircuits()
    {
        // Streaming array: 3 elements (:1, :2, :3), then extra elements
        // Short-circuiting means we can return false without reading all elements
        var data = "*?\r\n:1\r\n:2\r\n:3\r\n:999\r\n:888\r\n.\r\n"u8.ToArray();
        var reader = new RespReader(new ReadOnlySequence<byte>(data));
        reader.MoveNext(RespPrefix.Array);

        // These should all return false via short-circuiting
        // (we know the answer before reading all elements)
        Assert.False(reader.AggregateLengthIs(0)); // can tell after 1 element
        Assert.False(reader.AggregateLengthIs(2)); // can tell after 3 elements
        Assert.False(reader.AggregateLengthIs(4)); // can tell after 4 elements (count > expected)
        Assert.False(reader.AggregateLengthIs(10)); // can tell after 4 elements (count > expected)

        // The actual length is 5 (:1, :2, :3, :999, :888)
        Assert.True(reader.AggregateLengthIs(5));
    }

    [Fact] // streaming array - verify short-circuiting stops before reading malformed data
    public void AggregateLengthIs_Streaming_MalformedAfterShortCircuit()
    {
        // Streaming array: 3 elements (:1, :2, :3), then :4, then MALFORMED
        // To check if length == N, we need to read N+1 elements to verify there isn't more
        // So malformed data must come AFTER the N+1 threshold
        var data = "*?\r\n:1\r\n:2\r\n:3\r\n:4\r\nGARBAGE_NOT_A_VALID_ELEMENT"u8.ToArray();
        var reader = new RespReader(new ReadOnlySequence<byte>(data));
        reader.MoveNext(RespPrefix.Array);

        // When checking length < 4, we read up to 4 elements, see 4 > expected, stop
        Assert.False(reader.AggregateLengthIs(0)); // reads :1 (1 element), 1 > 0, stops before :2
        Assert.False(reader.AggregateLengthIs(2)); // reads :1, :2, :3 (3 elements), 3 > 2, stops before :4
        Assert.False(reader.AggregateLengthIs(3)); // reads :1, :2, :3, :4 (4 elements), 4 > 3, stops before MALFORMED

        // All of the above should succeed without hitting MALFORMED because we short-circuit
    }

    [Theory, Resp("%2\r\n+first\r\n:1\r\n+second\r\n:2\r\n", "%?\r\n+first\r\n:1\r\n+second\r\n:2\r\n.\r\n")] // map (simple and streaming)
    public void AggregateLengthIs_Map(RespPayload payload)
    {
        var reader = payload.Reader();
        reader.MoveNext(RespPrefix.Map);
        // Map length is doubled (2 pairs = 4 elements)
        Assert.True(reader.AggregateLengthIs(4));
        Assert.False(reader.AggregateLengthIs(0));
        Assert.False(reader.AggregateLengthIs(2));
        Assert.False(reader.AggregateLengthIs(3));
        Assert.False(reader.AggregateLengthIs(5));
        reader.SkipChildren();
        reader.DemandEnd();
    }

    [Theory, Resp("~5\r\n+orange\r\n+apple\r\n#t\r\n:100\r\n:999\r\n", "~?\r\n+orange\r\n+apple\r\n#t\r\n:100\r\n:999\r\n.\r\n")] // set (simple and streaming)
    public void AggregateLengthIs_Set(RespPayload payload)
    {
        var reader = payload.Reader();
        reader.MoveNext(RespPrefix.Set);
        Assert.True(reader.AggregateLengthIs(5));
        Assert.False(reader.AggregateLengthIs(0));
        Assert.False(reader.AggregateLengthIs(4));
        Assert.False(reader.AggregateLengthIs(6));
        reader.SkipChildren();
        reader.DemandEnd();
    }

    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        public override string ToString() => RespConstants.UTF8.GetString(Memory.Span)
            .Replace("\r", "\\r").Replace("\n", "\\n");

        public Segment(ReadOnlyMemory<byte> value, Segment? head)
        {
            Memory = value;
            if (head is not null)
            {
                RunningIndex = head.RunningIndex + head.Memory.Length;
                head.Next = this;
            }
        }
        public bool IsEmpty => Memory.IsEmpty;
        public int Length => Memory.Length;
    }
}
