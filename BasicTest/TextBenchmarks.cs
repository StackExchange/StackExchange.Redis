using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using BenchmarkDotNet.Attributes;

#pragma warning disable CS1591
namespace BasicTest
{
    [Config(typeof(CustomConfig))]
    public class TextBenchmarks
    {
        private readonly string[] corpus;
        private readonly byte[] buffer;
        public TextBenchmarks()
        {
            corpus = File.ReadAllLines("t8.shakespeare.txt");
            buffer = new byte[enc.GetMaxByteCount(corpus.Max(x => x.Length))];
        }
        private static readonly Encoding enc = Encoding.UTF8;

        [Benchmark]
        public long Measure()
        {
            long total = 0;
            for (int i = 0; i < corpus.Length; i++)
                total += enc.GetByteCount(corpus[i]);
            return total;
        }
        [Benchmark]
        public long MeasureAndEncode()
        {
            long total = 0;
            var buffer = this.buffer;
            for (int i = 0; i < corpus.Length; i++)
            {
                string s = corpus[i];
                total += enc.GetByteCount(s);
                enc.GetBytes(s, 0, s.Length, buffer, 0);
            }
            return total;
        }
        [Benchmark]
        public long MeasureVectorized()
        {
            long total = 0;
            for (int i = 0; i < corpus.Length; i++)
                total += GetEncodedLength(corpus[i], out _);
            return total;
        }

        [Benchmark]
        public long MeasureAndEncodeVectorized()
        {
            long total = 0;
            var buffer = this.buffer;
            for (int i = 0; i < corpus.Length; i++)
            {
                string s = corpus[i];
                total += GetEncodedLength(s, out var asciiChunks);
                Encode(s, buffer, asciiChunks);
            }
            return total;
        }

        private static readonly Vector<ushort> NonAsciiMask = new Vector<ushort>(0xFF80);
        internal static
#if NET47
            unsafe
#endif
            int GetEncodedLength(string value, out int asciiChunks)
        {
            asciiChunks = 0;
            if (value.Length == 0) return 0;
            int offset = 0;
            if (Vector.IsHardwareAccelerated && value.Length >= Vector<ushort>.Count)
            {
                var charSpan = MemoryMarshal.Cast<char, Vector<ushort>>(value.AsSpan());
                var nonAscii = NonAsciiMask;
                int i;
                for (i = 0; i < charSpan.Length; i++)
                {
                    if ((charSpan[i] & nonAscii) != Vector<ushort>.Zero) break;
                }
                offset = Vector<ushort>.Count * i;
                asciiChunks = i;
            }
            int remaining = value.Length - offset;
            if (remaining == 0) return offset; // all ASCII (nice round length, and Vector support)

            // handles a) no Vector support, b) anything from the fisrt non-ASCII chunk, c) tail end
#if NET47
            fixed (char* ptr = value)
            {
                return offset + Encoding.UTF8.GetByteCount(ptr + offset, remaining);
            }
#else
            return offset + enc.GetByteCount(s: value, index: offset, count: remaining);
#endif
        }

        private int Encode(string value, byte[] buffer, int asciiChunks)
        {
            int offset = 0;
            if (Vector.IsHardwareAccelerated && asciiChunks != 0)
            {
                var charSpan = MemoryMarshal.Cast<char, Vector<ushort>>(value.AsSpan());
                var byteSpan = MemoryMarshal.Cast<byte, Vector<byte>>(buffer);
                var nonAscii = NonAsciiMask;
                int i = 0;
                asciiChunks >>= 1; // half it - we can only use double-chunks

                for (int chunk = 0; chunk < asciiChunks; chunk++)
                {
                    byteSpan[chunk] = Vector.Narrow(charSpan[i++], charSpan[i++]);
                }
                offset = Vector<ushort>.Count * i;
                asciiChunks = i;
            }

            int remaining = value.Length - offset;
            if (remaining == 0) return offset; // all ASCII (nice round length, and Vector support)

            // handles a) no Vector support, b) anything from the fisrt non-ASCII chunk, c) tail end
            return offset + enc.GetBytes(value, offset, remaining, buffer, offset);
        }
    }
}
