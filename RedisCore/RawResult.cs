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
        // runs on uv thread
        public static unsafe bool TryParse(ref ReadableBuffer buffer, out RawResult result)
        {
            if (buffer.Length < 3)
            {
                result = default(RawResult);
                return false;
            }
            var span = buffer.FirstSpan;
            byte resultType = span.Array[span.Offset];
            switch (resultType)
            {
                case (byte)'+': // simple string
                    return TryReadLineTerminatedString(ResultType.SimpleString, ref buffer, out result);
                case (byte)'-': // error
                    return TryReadLineTerminatedString(ResultType.Error, ref buffer, out result);
                case (byte)':': // integer
                    return TryReadLineTerminatedString(ResultType.Integer, ref buffer, out result);
                case (byte)'$': // bulk string
                    throw new NotImplementedException();
                //return ReadBulkString(buffer, ref offset, ref count);
                case (byte)'*': // array
                    throw new NotImplementedException();
                //return ReadArray(buffer, ref offset, ref count);
                default:
                    throw new InvalidOperationException("Unexpected response prefix: " + (char)resultType);
            }
        }
        private static Vector<byte> _vectorCRs = new Vector<byte>((byte)'\r');
        private static bool TryReadLineTerminatedString(ResultType resultType, ref ReadableBuffer buffer, out RawResult result)
        {
            // look for the CR in the CRLF
            var seekBuffer = buffer;
            while (seekBuffer.Length >= 2)
            {
                var cr = seekBuffer.IndexOf(ref _vectorCRs);
                if (cr.IsEnd)
                {
                    result = default(RawResult);
                    return false;
                }
                // confirm that the LF in the CRLF
                var tmp = seekBuffer.Slice(cr).Slice(1);
                if (tmp.Peek() == (byte)'\n')
                {
                    result = new RawResult(resultType, buffer.Slice(1, cr));
                    buffer = tmp.Slice(1); // skip the \n next time
                    return true;
                }
                seekBuffer = tmp;
            }
            result = default(RawResult);
            return false;
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
