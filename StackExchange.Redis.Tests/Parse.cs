using System;
using System.Buffers;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class Parse : TestBase
    {
        public Parse(ITestOutputHelper output) : base(output) { }
        [Theory]
        [InlineData("$4\r\nPING\r\n$4\r\nPONG\r\n$4\r\r", 2)]
        public void ParseAsSingleChunk(string ascii, int expected)
        {
            ReadOnlySequence<byte> buffer = new ReadOnlySequence<byte>(Encoding.ASCII.GetBytes(ascii));


            var reader = new BufferReader(buffer);
            RawResult result;
            int found = 0;
            while(!(result = PhysicalConnection.TryParseResult(buffer, ref reader, false, null, false)).IsNull)
            {
                Writer.WriteLine($"{result} - {result.GetString()}");
                found++;
            }
            Assert.Equal(expected, found);
        }

        class FragmentedSegment : ReadOnlySequenceSegment<byte>
        {
            public FragmentedSegment(long runningIndex, ReadOnlyMemory<byte> memory)
            {
                RunningIndex = runningIndex;
                Memory = memory;
            }
            public new FragmentedSegment Next
            {
                get => (FragmentedSegment)base.Next;
                set => base.Next = value;
            }
        }
        [Theory]
        [InlineData("$4\r\nPING\r\n$4\r\nPONG\r\n$4\r\r", 2)]
        public void ParseAsLotsOfChunks(string ascii, int messages)
        {
            var bytes = Encoding.ASCII.GetBytes(ascii);
            FragmentedSegment chain = null, tail = null;
            for(int i = 0; i < bytes.Length; i++)
            {
                var next = new FragmentedSegment(i, new ReadOnlyMemory<byte>(bytes, i, 1));
                if(tail == null)
                {
                    chain = next;
                }
                else
                {
                    tail.Next = next;
                }
                tail = next;
            }

            ReadOnlySequence<byte> buffer = new ReadOnlySequence<byte>(chain, 0, tail, 0);
            Writer.WriteLine($"chain: {buffer.Length}");


            var reader = new BufferReader(buffer);
            RawResult result;
            int found = 0;
            while (!(result = PhysicalConnection.TryParseResult(buffer, ref reader, false, null, false)).IsNull)
            {
                Writer.WriteLine($"{result} - {result.GetString()}");
                found++;
            }
            Assert.Equal(messages, found);
        }
    }
}
