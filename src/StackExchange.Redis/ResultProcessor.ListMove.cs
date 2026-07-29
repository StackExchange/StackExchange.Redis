using RESPite.Messages;

// ReSharper disable once CheckNamespace
namespace StackExchange.Redis;

internal abstract partial class ResultProcessor
{
    /// <summary>
    /// Parses the reply of <c>LMOVEM</c>: an array of moved elements, or <see langword="null"/> when
    /// <c>EXACTLY</c> could not be satisfied. A null reply is preserved as <see langword="null"/>, distinct
    /// from an empty array (nothing moved under <c>COUNT</c>).
    /// </summary>
    public static readonly ResultProcessor<RedisValue[]?>
        NullableRedisValueArray = new NullableRedisValueArrayProcessor();

    private sealed class NullableRedisValueArrayProcessor : ResultProcessor<RedisValue[]?>
    {
        protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
        {
            if (reader.IsNull)
            {
                SetResult(message, null);
                return true;
            }
            if (reader.IsAggregate)
            {
                var arr = reader.ReadPastRedisValues() ?? [];
                SetResult(message, arr);
                return true;
            }
            return false; // scalar / other => unexpected
        }
    }
}
