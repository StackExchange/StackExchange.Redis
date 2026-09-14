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
            // the shape lives on the type it produces, so the interpolated surface's handler reads the
            // identical reply the identical way; see ValueCondition.TryReadDigest
            if (!ValueCondition.TryReadDigest(in reader, out var digest)) return false;

            SetResult(message, digest);
            return true;
        }
    }
}
