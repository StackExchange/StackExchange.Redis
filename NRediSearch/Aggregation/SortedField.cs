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

        public static SortedField Ascending(string field) => new SortedField(field, Order.Ascending);
        public static SortedField Descending(string field) => new SortedField(field, Order.Descending);
    }
}
