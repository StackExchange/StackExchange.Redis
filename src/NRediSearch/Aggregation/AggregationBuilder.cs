// .NET port of https://github.com/RedisLabs/JRediSearch/
using System.Linq;
using System.Collections.Generic;
using NRediSearch.Aggregation.Reducers;

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

        public AggregationBuilder SortBy(int max, params SortedField[] fields)
        {
            SortBy(fields);

            if (max > 0)
            {
                args.Add("MAX");
                args.Add(max.ToString());
            }

            return this;
        }

        public AggregationBuilder SortByAscending(string field) => SortBy(SortedField.Ascending(field));

        public AggregationBuilder SortByDescending(string field) => SortBy(SortedField.Descending(field));

        public AggregationBuilder Apply(string projection, string alias)
        {
            args.Add("APPLY");
            args.Add(projection);
            args.Add("AS");
            args.Add(alias);

            return this;
        }

        public AggregationBuilder GroupBy(IReadOnlyCollection<string> fields, IReadOnlyCollection<Reducer> reducers)
        {
            var group = new Group(fields.ToArray());

            foreach(var r in reducers)
            {
                group.Reduce(r);
            }

            GroupBy(group);

            return this;
        }

        public AggregationBuilder GroupBy(string field, params Reducer[] reducers) => GroupBy(new[] { field }, reducers);

        public AggregationBuilder GroupBy(Group group)
        {
            args.Add("GROUPBY");

            group.SerializeRedisArgs(args);

            return this;
        }

        public AggregationBuilder Filter(string expression)
        {
            args.Add("FILTER");
            args.Add(expression);

            return this;
        }

        // TODO: cursor(int count, long maxIdle)

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
