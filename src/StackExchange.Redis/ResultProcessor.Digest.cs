using System;
using System.Buffers;
using RESPite.Messages;

namespace StackExchange.Redis;

internal abstract partial class ResultProcessor
{
    // VectorSet result processors
    public static readonly ResultProcessor<ValueCondition?> Digest =
        new DigestProcessor();

    private sealed class DigestProcessor : ResultProcessor<ValueCondition?>
    {
        protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
        {
            if (reader.IsNull) // for example, key doesn't exist
            {
                SetResult(message, null);
                return true;
            }

            if (reader.ScalarLengthIs(2 * ValueCondition.DigestBytes))
            {
                var span = reader.TryGetSpan(out var tmp) ? tmp : reader.Buffer(stackalloc byte[2 * ValueCondition.DigestBytes]);
                var digest = ValueCondition.ParseDigest(span);
                SetResult(message, digest);
                return true;
            }
            return false;
        }
    }
}
