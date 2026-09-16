using System;
using System.Buffers;
using System.Text;
using BenchmarkDotNet.Attributes;

namespace StackExchange.Redis.Benchmarks
{
    /// <summary>
    /// Sizes the cost of <see cref="RedisValue"/> equality where one side is a string and the other a blob.
    /// That mixed case currently decodes the blob into a transient string (see operator ==), which is the
    /// allocation PR #3116 set out to remove; this measures whether it is worth removing, and - via the
    /// DiffAtStart case - how much is lost by comparing only after the whole payload has been materialised.
    /// </summary>
    [Config(typeof(SlowConfig))]
    public class RedisValueEqualityBenchmarks
    {
        public enum Shape
        {
            /// <summary>Both sides identical; the full payload has to be examined either way.</summary>
            Equal,

            /// <summary>Differs in the first byte: everything after it is wasted work.</summary>
            DiffAtStart,

            /// <summary>Differs in the last byte: the whole payload must be examined regardless.</summary>
            DiffAtEnd,
        }

        [Params(16, 1024, 65536)]
        public int Size { get; set; }

        [Params(Shape.Equal, Shape.DiffAtStart, Shape.DiffAtEnd)]
        public Shape Form { get; set; }

        private RedisValue _string, _other, _byteArray, _sequence, _stringB;

        private static string MakeString(int size, Shape form)
        {
            // deliberately non-numeric: anything numeric is reduced by Simplify() and never reaches the
            // string/blob comparison at all
            var chars = new char[size];
            for (int i = 0; i < size; i++) chars[i] = (char)('a' + (i % 26));
            var s = new string(chars);
            return form switch
            {
                Shape.DiffAtStart => "Z" + s.Substring(1),
                Shape.DiffAtEnd => s.Substring(0, size - 1) + "Z",
                _ => s,
            };
        }

        private static ReadOnlySequence<byte> AsSegmented(byte[] bytes)
        {
            // split in the middle, so the multi-segment path is exercised rather than the fast single-span one
            int mid = bytes.Length / 2;
            var first = new Segment(new ReadOnlyMemory<byte>(bytes, 0, mid), null);
            var second = new Segment(new ReadOnlyMemory<byte>(bytes, mid, bytes.Length - mid), first);
            return new ReadOnlySequence<byte>(first, 0, second, second.Memory.Length);
        }

        [GlobalSetup]
        public void Setup()
        {
            var baseline = MakeString(Size, Shape.Equal);
            var variant = MakeString(Size, Form);

            _string = baseline;
            _stringB = variant;

            var bytes = Encoding.UTF8.GetBytes(variant);
            _byteArray = bytes;
            _sequence = AsSegmented(bytes);
            _other = Encoding.UTF8.GetBytes(baseline);
        }

        /// <summary>String vs byte[]: the mixed case #3116 targets.</summary>
        [Benchmark(Baseline = true)]
        public bool StringVsByteArray() => _string == _byteArray;

        /// <summary>String vs a multi-segment blob - same mixed case, sequence-backed.</summary>
        [Benchmark]
        public bool StringVsSequence() => _string == _sequence;

        /// <summary>Control: blob vs blob already compares by raw bytes, with no decode.</summary>
        [Benchmark]
        public bool BlobVsBlob() => _other == _byteArray;

        /// <summary>Control: string vs string, the plain managed comparison.</summary>
        [Benchmark]
        public bool StringVsString() => _string == _stringB;

        /// <summary>Hashing a blob: decodes the whole payload into a pooled char buffer.</summary>
        [Benchmark]
        public int HashBlob() => _byteArray.GetHashCode();

        /// <summary>Hashing the equivalent string, for comparison.</summary>
        [Benchmark]
        public int HashString() => _string.GetHashCode();

        private sealed class Segment : ReadOnlySequenceSegment<byte>
        {
            public Segment(ReadOnlyMemory<byte> value, Segment? head)
            {
                Memory = value;
                if (head is not null)
                {
                    RunningIndex = head.RunningIndex + head.Memory.Length;
                    head.Next = this;
                }
            }
        }
    }
}
