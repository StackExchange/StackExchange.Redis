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
        [Fact]
        public void EvilParse()
        {
            ReadOnlySequence<byte> buffer = new ReadOnlySequence<byte>(Encoding.ASCII.GetBytes($"$4\r\nPING\r\n$4\r\nPONG\r\n"));


            var reader = new BufferReader(buffer);
            RawResult result;
            int found = 0;
            while(!(result = PhysicalConnection.TryParseResult(buffer, ref reader, false, null, false)).IsNull)
            {
                Writer.WriteLine($"{result} - {result.GetString()}");
                found++;
            }
            Assert.Equal(2, found);
        }

        class FragmentedSegment : ReadOnlySequenceSegment<byte>
        {
            public new FragmentedSegment Next
            {
                get => (FragmentedSegment)base.Next;
                set => base.Next = value;
            }
            public new ReadOnlyMemory<byte> Memory
            {
                get => base.Memory;
                set => base.Memory = value;
            }
            public new long RunningIndex
            {
                get => base.RunningIndex;
                set => base.RunningIndex = value;
            }
        }
        [Fact]
        public void EvilParse2()
        {
            var bytes = Encoding.ASCII.GetBytes($"$4\r\nPING\r\n$4\r\nPONG\r\n");
            FragmentedSegment chain = null, tail = null;
            for(int i = 0; i < bytes.Length; i++)
            {
                var next = new FragmentedSegment();
                next.RunningIndex = i;
                next.Memory = new ReadOnlyMemory<byte>(bytes, i, 1);
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
            Assert.Equal(2, found);
        }
    }
}
