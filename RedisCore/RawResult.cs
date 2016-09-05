using Channels;
using Channels.Text.Primitives;
using System;
using System.Numerics;

namespace RedisCore
{
    struct RawResult
    {
        public override string ToString()
        {
            switch (Type)
            {
                case ResultType.SimpleString:
                case ResultType.Integer:
                case ResultType.Error:
                    return $"{Type}: {Buffer.GetAsciiString()}";
                case ResultType.BulkString:
                    return $"{Type}: {Buffer.Length} bytes";
                case ResultType.MultiBulk:
                    return $"{Type}: {Items.Length} items";
                default:
                    return "(unknown)";
            }
        }
        internal string GetAsciiString() => Buffer.Length == 0 ? "" : Buffer.GetAsciiString();
        internal string GetUtf8String() => Buffer.Length == 0 ? "" : Buffer.GetUtf8String();
        // runs on uv thread
        public static unsafe bool TryParse(ref ReadableBuffer buffer, out RawResult result)
        {
            if (buffer.Length < 3)
            {
                result = default(RawResult);
                return false;
            }
            byte resultType = (byte)buffer.Peek();
            switch (resultType)
            {
                case (byte)'+': // simple string
                    return TryReadLineTerminatedString(ResultType.SimpleString, ref buffer, out result);
                case (byte)'-': // error
                    return TryReadLineTerminatedString(ResultType.Error, ref buffer, out result);
                case (byte)':': // integer
                    return TryReadLineTerminatedString(ResultType.Integer, ref buffer, out result);
                case (byte)'$': // bulk string
                    return TryReadBulkString(ref buffer, out result);
                case (byte)'*': // array
                    throw new NotImplementedException();
                //return ReadArray(buffer, ref offset, ref count);
                default:
                    throw new InvalidOperationException("Unexpected response prefix: " + (char)resultType);
            }
        }

        private static Vector<byte> _vectorCRs = new Vector<byte>((byte)'\r'),
            _unused = new Vector<byte>(0); // to avoid JIT bug in net 452; yes, really

        private static bool TryReadLineTerminatedString(ResultType resultType, ref ReadableBuffer buffer, out RawResult result)
        {
            ReadCursor cr;
            if (TryFindCRLF(ref buffer, out cr))
            {
                result = new RawResult(resultType, buffer.Slice(1, cr));
                buffer = buffer.Slice(cr).Slice(2);
                return true;
            }
            result = default(RawResult);
            return false;
        }
        static bool TryFindCRLF(ref ReadableBuffer buffer, out ReadCursor cr)
        {
            var seekBuffer = buffer;
            while (seekBuffer.Length >= 2)
            {
                cr = seekBuffer.IndexOf(ref _vectorCRs);
                if (cr.IsEnd) break;

                // confirm that the LF in the CRLF
                var tmp = seekBuffer.Slice(cr);
                if (tmp.StartsWith(RedisConnection.CRLF, 0, RedisConnection.CRLF.Length))
                {
                    return true;
                }
                // move forwards and keep trying
                seekBuffer = tmp;
            }
            cr = default(ReadCursor);
            return false;
        }
        static readonly byte[] MinusOne = { (byte)'-', (byte)'1' };
        private static bool TryReadBulkString(ref ReadableBuffer buffer, out RawResult result)
        {
            ReadCursor cr;
            if (!TryFindCRLF(ref buffer, out cr))
            {
                result = default(RawResult);
                return false;
            }
            var slice = buffer.Slice(1, cr);
            if (slice.Peek() == (byte)'-')
            {
                if (slice.Equals(MinusOne, 0, MinusOne.Length))
                {
                    throw new NotImplementedException("Null bulk string");
                    // result = 
                    // return true;
                }
                throw new InvalidOperationException("Protocol exception; negative length not expected except -1");
            }
            var ulen = ReadableBufferExtensions.GetUInt64(slice);
            if (ulen > int.MaxValue) throw new OverflowException();
            var len = (int)ulen;

            // check that the final CRLF is well formed
            slice = buffer.Slice(cr).Slice(2);
            if (slice.Length < len + 2)
            {
                // not enough data
                result = default(RawResult);
                return false;
            }

            var tmp = slice.Slice(len);
            if (tmp.Peek() != '\r' || tmp.Slice(1).Peek() != '\n')
            {
                throw new InvalidOperationException("Protocol exception; expected crlf after bulk string");
            }

            // all looks good, yay!
            result = new RawResult(ResultType.BulkString, slice.Slice(0, len));
            buffer = slice.Slice(len + 2);
            return true;
        }

        public RawResult(RawResult[] items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            Type = ResultType.MultiBulk;
            Items = items;
            Buffer = default(ReadableBuffer);
        }
        private RawResult(ResultType resultType, ReadableBuffer buffer)
        {
            switch (resultType)
            {
                case ResultType.SimpleString:
                case ResultType.Error:
                case ResultType.Integer:
                case ResultType.BulkString:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(resultType));
            }
            Type = resultType;
            Buffer = buffer;
            Items = null;
        }
        public readonly ResultType Type;
        public
#if DEBUG
            readonly
#endif
            ReadableBuffer Buffer;
        private readonly RawResult[] Items;
    }
}
