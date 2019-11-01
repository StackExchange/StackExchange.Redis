// .NET port of https://github.com/RedisLabs/JRediSearch/

using System.Collections.Generic;

namespace NRediSearch.Aggregation
{
    public class AggregationBuilder
    {
        private readonly List<object> args = new List<object>();

        public AggregationBuilder() : this("*")
        {
        }

        public AggregationBuilder(string query) => args.Add(query);

        public AggregationBuilder Load(params string[] fields)
        {
            AddCommandArguments(args, "LOAD", fields);

            return this;
        }

        public AggregationBuilder Limit(int offset, int count)
        {
            var limit = new Limit(offset, count);

            limit.SerializeRedisArgs(args);

            return this;
        }

        public AggregationBuilder Limit(int count) => Limit(0, count);

        public AggregationBuilder SortBy(params SortedField[] fields)
        {
            args.Add("SORTBY");
            args.Add((fields.Length * 2).ToString());

            foreach (var field in fields)
            {
                args.Add(field.Field);
                args.Add(field.Order);
            }

            return this;
        }

        private static void AddCommandLength(List<object> list, string command, int length)
        {
            list.Add(command);
            list.Add(length.ToString());
        }

        private static void AddCommandArguments(List<object> destination, string command, IReadOnlyCollection<object> source)
        {
            AddCommandLength(destination, command, source.Count);
            destination.AddRange(source);
        }
    }
}
