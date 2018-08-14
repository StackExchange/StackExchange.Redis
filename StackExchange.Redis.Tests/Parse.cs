using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class Parse : TestBase
    {
        public Parse(ITestOutputHelper output) : base(output) { }

        public static IEnumerable<object[]> GetTestData()
        {
            yield return new object[] { "$4\r\nPING\r\n$4\r\nPONG\r\n", 2 };
            yield return new object[] { "$4\r\nPING\r\n$4\r\nPONG\r\n$4\r", 2 };
        }
        [Theory]
        [MemberData(nameof(GetTestData))]
        public void ParseAsSingleChunk(string ascii, int expected)
        {
            var buffer = new ReadOnlySequence<byte>(Encoding.ASCII.GetBytes(ascii));
            ProcessMessages(buffer, expected);
        }


        [Theory]
        [MemberData(nameof(GetTestData))]
        public void ParseAsLotsOfChunks(string ascii, int expected)
        {
            var bytes = Encoding.ASCII.GetBytes(ascii);
            FragmentedSegment<byte> chain = null, tail = null;
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
            var buffer = new ReadOnlySequence<byte>(chain, 0, tail, 0);
            ProcessMessages(buffer, expected);


        }
        void ProcessMessages(ReadOnlySequence<byte> buffer, int expected)
        {
            Writer.WriteLine($"chain: {buffer.Length}");
            var reader = new BufferReader(buffer);
            RawResult result;
            int found = 0;
            while (!(result = PhysicalConnection.TryParseResult(buffer, ref reader, false, null, false)).IsNull)
            {
                Writer.WriteLine($"{result} - {result.GetString()}");
                found++;
            }
            Assert.Equal(expected, found);
        }


        class FragmentedSegment<T> : ReadOnlySequenceSegment<T>
        {
            public FragmentedSegment(long runningIndex, ReadOnlyMemory<T> memory)
            {
                RunningIndex = runningIndex;
                Memory = memory;
            }
            public new FragmentedSegment<T> Next
            {
                get => (FragmentedSegment<T>)base.Next;
                set => base.Next = value;
            }
        }
    }
}
