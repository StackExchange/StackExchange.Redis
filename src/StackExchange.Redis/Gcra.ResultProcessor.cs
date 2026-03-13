using RESPite.Messages;

namespace StackExchange.Redis;

public readonly partial struct GcraRateLimitResult
{
    internal static readonly ResultProcessor<GcraRateLimitResult> Processor = new GcraRateLimitResultProcessor();

    private sealed class GcraRateLimitResultProcessor : ResultProcessor<GcraRateLimitResult>
    {
        protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
        {
            // GCRA returns an array with 5 elements:
            // 1) <limited> # 0 or 1
            // 2) <max-req-num> # max number of request. Always equal to max_burst+1
            // 3) <num-avail-req> # number of requests available immediately
            // 4) <reply-after> # number of seconds after which caller should retry. Always returns -1 if request isn't limited.
            // 5) <full-burst-after> # number of seconds after which a full burst will be allowed
            if (reader.IsAggregate
                && reader.TryMoveNext() && reader.IsScalar && reader.TryReadBoolean(out bool limited)
                && reader.TryMoveNext() && reader.IsScalar && reader.TryReadInt64(out long maxRequests)
                && reader.TryMoveNext() && reader.IsScalar && reader.TryReadInt64(out long availableRequests)
                && reader.TryMoveNext() && reader.IsScalar && reader.TryReadInt64(out long retryAfterSeconds)
                && reader.TryMoveNext() && reader.IsScalar && reader.TryReadInt64(out long fullBurstAfterSeconds))
            {
                var result = new GcraRateLimitResult(
                    limited: limited,
                    maxRequests: (int)maxRequests,
                    availableRequests: (int)availableRequests,
                    retryAfterSeconds: (int)retryAfterSeconds,
                    fullBurstAfterSeconds: (int)fullBurstAfterSeconds);
                SetResult(message, result);
                return true;
            }

            return false;
        }
    }
}
