// .NET port of https://github.com/RedisLabs/JRediSearch/
using StackExchange.Redis;

namespace NRediSearch.Aggregation
{
    public readonly struct SortedField
    {
        public SortedField(string field, Order order)
        {
            Field = field;
            Order = order;
        }

        public string Field { get; }
        public Order Order { get; }

        internal object OrderAsArg() => (Order == Order.Ascending ? "ASC" : "DESC").Literal();
    }
}
