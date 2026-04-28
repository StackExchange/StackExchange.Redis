namespace StackExchange.Redis;

internal static class IncrexResultProcessor
{
    internal static readonly ResultProcessor<StringIncrementResult<long>> Int64 = new Int64ResultProcessor();
    internal static readonly ResultProcessor<StringIncrementResult<double>> Double = new DoubleResultProcessor();

    private sealed class Int64ResultProcessor : ResultProcessor<StringIncrementResult<long>>
    {
        protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
        {
            if (result.Resp2TypeArray == ResultType.Array && result.ItemsCount >= 2)
            {
                var items = result.GetItems();
                if (items[0].TryGetInt64(out long value) && items[1].TryGetInt64(out long appliedIncrement))
                {
                    SetResult(message, new StringIncrementResult<long>(value, appliedIncrement));
                    return true;
                }
            }
            return false;
        }
    }

    private sealed class DoubleResultProcessor : ResultProcessor<StringIncrementResult<double>>
    {
        protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
        {
            if (result.Resp2TypeArray == ResultType.Array && result.ItemsCount >= 2)
            {
                var items = result.GetItems();
                if (items[0].TryGetDouble(out double value) && items[1].TryGetDouble(out double appliedIncrement))
                {
                    SetResult(message, new StringIncrementResult<double>(value, appliedIncrement));
                    return true;
                }
            }
            return false;
        }
    }
}
