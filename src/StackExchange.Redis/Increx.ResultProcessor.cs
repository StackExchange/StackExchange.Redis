using RESPite.Messages;

namespace StackExchange.Redis;

internal static class IncrexResultProcessor
{
    internal static readonly ResultProcessor<StringIncrementResult<long>> Int64 = new Int64ResultProcessor();
    internal static readonly ResultProcessor<StringIncrementResult<double>> Double = new DoubleResultProcessor();

    private sealed class Int64ResultProcessor : ResultProcessor<StringIncrementResult<long>>
    {
        protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
        {
            if (reader.IsAggregate
                && reader.TryMoveNext() && reader.IsScalar && reader.TryReadInt64(out long value)
                && reader.TryMoveNext() && reader.IsScalar && reader.TryReadInt64(out long appliedIncrement))
            {
                SetResult(message, new StringIncrementResult<long>(value, appliedIncrement));
                return true;
            }
            return false;
        }
    }

    private sealed class DoubleResultProcessor : ResultProcessor<StringIncrementResult<double>>
    {
        protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
        {
            if (reader.IsAggregate
                && reader.TryMoveNext() && reader.IsScalar && reader.TryReadDouble(out double value)
                && reader.TryMoveNext() && reader.IsScalar && reader.TryReadDouble(out double appliedIncrement))
            {
                SetResult(message, new StringIncrementResult<double>(value, appliedIncrement));
                return true;
            }
            return false;
        }
    }
}
