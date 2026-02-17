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
        protected override bool SetResultCore(PhysicalConnection connection, Message message, RawResult result)
        {
            if (result.IsNull) // for example, key doesn't exist
            {
                SetResult(message, null);
                return true;
            }

            if (result.Resp2TypeBulkString == ResultType.BulkString
                && result.Payload is { Length: 2 * ValueCondition.DigestBytes } payload)
            {
                ValueCondition digest;
                if (payload.IsSingleSegment) // single chunk - fast path
                {
                    digest = ValueCondition.ParseDigest(payload.First.Span);
                }
                else // linearize
                {
                    Span<byte> buffer = stackalloc byte[2 * ValueCondition.DigestBytes];
                    payload.CopyTo(buffer);
                    digest = ValueCondition.ParseDigest(buffer);
                }
                SetResult(message, digest);
                return true;
            }
            return false;
        }
    }
}
