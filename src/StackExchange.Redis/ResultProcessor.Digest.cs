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
                    if (payload.Length == ValueCondition.DigestBytes * 2)
                    {
                        if (payload.IsSingleSegment)
                        {
                            SetResult(message, ValueCondition.ParseDigest(payload.First.Span));
                        }
                        else
                        {
                            // linearize (note we already checked the length)
                            Span<byte> copy = stackalloc byte[ValueCondition.DigestBytes * 2];
                            payload.CopyTo(copy);
                            SetResult(message, ValueCondition.ParseDigest(copy));
                        }
                        return true;
                    }

                    break;
            }
            return false;
        }
    }
}
