using System;
using System.Buffers;

namespace StackExchange.Redis;

internal abstract partial class ResultProcessor
{
    // VectorSet result processors
    public static readonly ResultProcessor<ValueCondition?> Digest =
        new DigestProcessor();

    private sealed class DigestProcessor : ResultProcessor<ValueCondition?>
    {
        protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
        {
            if (result.IsNull)
            {
                SetResult(message, null);
                return true;
            }

            switch (result.Resp2TypeBulkString)
            {
                case ResultType.BulkString:
                    var payload = result.Payload;
                    var len = checked((int)payload.Length);
                    if (len == 2 * ValueCondition.DigestBytes & payload.IsSingleSegment)
                    {
                        // full-size hash in a single chunk - fast path
                        SetResult(message, ValueCondition.ParseDigest(payload.First.Span));
                        return true;
                    }

                    if (len >= 1 & len <= ValueCondition.DigestBytes * 2)
                    {
                        // Either multi-segment, or isn't long enough (missing leading zeros,
                        // see https://github.com/redis/redis/issues/14496).
                        Span<byte> buffer = new byte[2 * ValueCondition.DigestBytes];
                        int start = (2 * ValueCondition.DigestBytes) - len;
                        if (start != 0) buffer.Slice(0, start).Fill((byte)'0'); // pad
                        payload.CopyTo(buffer.Slice(start)); // linearize
                        SetResult(message, ValueCondition.ParseDigest(buffer));
                        return true;
                    }
                    break;
            }
            return false;
        }
    }
}
