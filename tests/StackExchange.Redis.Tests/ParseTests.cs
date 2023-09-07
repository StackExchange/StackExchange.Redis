using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using Pipelines.Sockets.Unofficial.Arenas;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests;

public class ParseTests : TestBase
{
    public ParseTests(ITestOutputHelper output) : base(output) { }

    public static IEnumerable<object[]> GetTestData()
    {
        yield return new object[] { "$4\r\nPING\r\n$4\r\nPON", 1 };
        yield return new object[] { "$4\r\nPING\r\n$4\r\nPONG", 1 };
        yield return new object[] { "$4\r\nPING\r\n$4\r\nPONG\r", 1 };
        yield return new object[] { "$4\r\nPING\r\n$4\r\nPONG\r\n", 2 };
        yield return new object[] { "$4\r\nPING\r\n$4\r\nPONG\r\n$4", 2 };
        yield return new object[] { "$4\r\nPING\r\n$4\r\nPONG\r\n$4\r", 2 };
        yield return new object[] { "$4\r\nPING\r\n$4\r\nPONG\r\n$4\r\n", 2 };
        yield return new object[] { "$4\r\nPING\r\n$4\r\nPONG\r\n$4\r\nP", 2 };
        yield return new object[] { "$4\r\nPING\r\n$4\r\nPONG\r\n$4\r\nPO", 2 };
        yield return new object[] { "$4\r\nPING\r\n$4\r\nPONG\r\n$4\r\nPON", 2 };
        yield return new object[] { "$4\r\nPING\r\n$4\r\nPONG\r\n$4\r\nPONG", 2 };
        yield return new object[] { "$4\r\nPING\r\n$4\r\nPONG\r\n$4\r\nPONG\r", 2 };
        yield return new object[] { "$4\r\nPING\r\n$4\r\nPONG\r\n$4\r\nPONG\r\n", 3 };
        yield return new object[] { "$4\r\nPING\r\n$4\r\nPONG\r\n$4\r\nPONG\r\n$", 3 };
    }

    [Theory]
    [MemberData(nameof(GetTestData))]
    public void ParseAsSingleChunk(string ascii, int expected)
    {
        var buffer = new ReadOnlySequence<byte>(Encoding.ASCII.GetBytes(ascii));
        using (var arena = new Arena<RawResult>())
        {
            ProcessMessages(arena, buffer, expected);
        }
    }

    [Theory]
    [MemberData(nameof(GetTestData))]
    public void ParseAsLotsOfChunks(string ascii, int expected)
    {
        var bytes = Encoding.ASCII.GetBytes(ascii);
        FragmentedSegment<byte>? chain = null, tail = null;
        for (int i = 0; i < bytes.Length; i++)
        {
            var next = new FragmentedSegment<byte>(i, new ReadOnlyMemory<byte>(bytes, i, 1));
            if (tail == null)
            {
                chain = next;
            }
            else
            {
                tail.Next = next;
            }
            tail = next;
        }
        var buffer = new ReadOnlySequence<byte>(chain!, 0, tail!, 1);
        Assert.Equal(bytes.Length, buffer.Length);
        using (var arena = new Arena<RawResult>())
        {
            ProcessMessages(arena, buffer, expected);
        }
    }

    private void ProcessMessages(Arena<RawResult> arena, ReadOnlySequence<byte> buffer, int expected)
    {
        Log($"chain: {buffer.Length}");
        var reader = new BufferReader(buffer);
        RawResult result;
        int found = 0;
        while (!(result = PhysicalConnection.TryParseResult(false, arena, buffer, ref reader, false, null, false)).IsNull)
        {
            Log($"{result} - {result.GetString()}");
            found++;
        }
        Assert.Equal(expected, found);
    }

    private class FragmentedSegment<T> : ReadOnlySequenceSegment<T>
    {
        public FragmentedSegment(long runningIndex, ReadOnlyMemory<T> memory)
        {
            RunningIndex = runningIndex;
            Memory = memory;
        }

        public new FragmentedSegment<T>? Next
        {
            get => (FragmentedSegment<T>?)base.Next;
            set => base.Next = value;
        }
    }
}
