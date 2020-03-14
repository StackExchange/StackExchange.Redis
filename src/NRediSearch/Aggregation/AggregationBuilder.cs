// .NET port of https://github.com/RedisLabs/JRediSearch/
using System.Collections.Generic;
using System.Linq;
using NRediSearch.Aggregation.Reducers;

namespace NRediSearch.Aggregation
{
    public sealed class AggregationBuilder
    {
        private readonly List<object> _args = new List<object>();

        public bool IsWithCursor { get; private set; }

        internal string GetArgsString() => string.Join(" ", _args);

        public AggregationBuilder(string query = "*") => _args.Add(query);

        public AggregationBuilder Load(params string[] fields)
        {
            AddCommandArguments(_args, "LOAD", fields);

            return this;
        }

        public AggregationBuilder Limit(int offset, int count)
        {
            var limit = new Limit(offset, count);

            limit.SerializeRedisArgs(_args);

            return this;
        }

        public AggregationBuilder Limit(int count) => Limit(0, count);

        public AggregationBuilder SortBy(params SortedField[] fields)
        {
            _args.Add("SORTBY");
            _args.Add(fields.Length * 2);

            foreach (var field in fields)
            {
                _args.Add(field.Field);
                _args.Add(field.OrderAsArg());
            }

            return this;
        }

        public AggregationBuilder SortBy(int max, params SortedField[] fields)
        {
            SortBy(fields);

            if (max > 0)
            {
                _args.Add("MAX");
                _args.Add(max);
            }

            return this;
        }

        public AggregationBuilder SortByAscending(string field) => SortBy(SortedField.Ascending(field));

        public AggregationBuilder SortByDescending(string field) => SortBy(SortedField.Descending(field));

        public AggregationBuilder Apply(string projection, string alias)
        {
            _args.Add("APPLY");
            _args.Add(projection);
            _args.Add("AS");
            _args.Add(alias);

            return this;
        }

        public AggregationBuilder GroupBy(IReadOnlyCollection<string> fields, IReadOnlyCollection<Reducer> reducers)
        {
            var group = new Group(fields.ToArray());

            foreach (var r in reducers)
            {
                group.Reduce(r);
            }

            GroupBy(group);

            return this;
        }

        public AggregationBuilder GroupBy(string field, params Reducer[] reducers) => GroupBy(new[] { field }, reducers);

        public AggregationBuilder GroupBy(Group group)
        {
            _args.Add("GROUPBY");

            group.SerializeRedisArgs(_args);

            return this;
        }

        public AggregationBuilder Filter(string expression)
        {
            _args.Add("FILTER");
            _args.Add(expression);

            return this;
        }

        public AggregationBuilder Cursor(int count, long maxIdle)
        {
            IsWithCursor = true;

            if (count > 0)
            {
                _args.Add("WITHCURSOR");
                _args.Add("COUNT");
                _args.Add(count);

                if (maxIdle < long.MaxValue && maxIdle >= 0)
                {
                    _args.Add("MAXIDLE");
                    _args.Add(maxIdle);
                }
            }

            return this;
        }

        internal void SerializeRedisArgs(List<object> args)
        {
            foreach (var arg in _args)
            {
                args.Add(arg);
            }
        }

        private static void AddCommandLength(List<object> list, string command, int length)
        {
            list.Add(command);
            list.Add(length);
        }

        private static void AddCommandArguments(List<object> destination, string command, IReadOnlyCollection<object> source)
        {
            AddCommandLength(destination, command, source.Count);
            destination.AddRange(source);
        }
    }
}
