namespace StackExchange.Redis;

public readonly partial struct GcraRateLimitResult
{
    internal static readonly ResultProcessor<GcraRateLimitResult> Processor = new GcraRateLimitResultProcessor();

    private sealed class GcraRateLimitResultProcessor : ResultProcessor<GcraRateLimitResult>
    {
        protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
        {
            // GCRA returns an array with 5 elements:
            // 1) <limited> # 0 or 1
            // 2) <max-token-num> # max number of tokens. Always equal to max_burst+1
            // 3) <num-avail-token> # number of tokens available immediately
            // 4) <reply-after> # number of seconds after which caller should retry. Always returns -1 if acquisition isn't limited.
            // 5) <full-burst-after> # number of seconds after which a full burst will be allowed
            if (result.Resp2TypeArray == ResultType.Array && result.ItemsCount >= 5)
            {
                var items = result.GetItems();
                bool limited = items[0].GetBoolean();
                if (items[1].TryGetInt64(out long maxTokens)
                    && items[2].TryGetInt64(out long availableTokens)
                    && items[3].TryGetInt64(out long retryAfterSeconds)
                    && items[4].TryGetInt64(out long fullBurstAfterSeconds))
                {
                    var grca = new GcraRateLimitResult(
                        limited: limited,
                        maxTokens: (int)maxTokens,
                        availableTokens: (int)availableTokens,
                        retryAfterSeconds: (int)retryAfterSeconds,
                        fullBurstAfterSeconds: (int)fullBurstAfterSeconds);
                    SetResult(message, grca);
                    return true;
                }
            }

            return false;
        }

        /* for v3, already done (due to branch choice)
        protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
        {
            // GCRA returns an array with 5 elements:
            // 1) <limited> # 0 or 1
            // 2) <max-token-num> # max number of tokens. Always equal to max_burst+1
            // 3) <num-avail-token> # number of tokens available immediately
            // 4) <reply-after> # number of seconds after which caller should retry. Always returns -1 if acquisition isn't limited.
            // 5) <full-burst-after> # number of seconds after which a full burst will be allowed
            if (reader.IsAggregate
                && reader.TryMoveNext() && reader.IsScalar && reader.TryReadBoolean(out bool limited)
                && reader.TryMoveNext() && reader.IsScalar && reader.TryReadInt64(out long maxTokens)
                && reader.TryMoveNext() && reader.IsScalar && reader.TryReadInt64(out long availableTokens)
                && reader.TryMoveNext() && reader.IsScalar && reader.TryReadInt64(out long retryAfterSeconds)
                && reader.TryMoveNext() && reader.IsScalar && reader.TryReadInt64(out long fullBurstAfterSeconds))
            {
                var result = new GcraRateLimitResult(
                    limited: limited,
                    maxTokens: (int)maxTokens,
                    availableTokens: (int)availableTokens,
                    retryAfterSeconds: (int)retryAfterSeconds,
                    fullBurstAfterSeconds: (int)fullBurstAfterSeconds);
                SetResult(message, result);
                return true;
            }

            return false;
        }
        */
    }
}
