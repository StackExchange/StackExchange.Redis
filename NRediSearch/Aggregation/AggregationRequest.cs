// .NET port of https://github.com/RedisLabs/JRediSearch/

using System;
using System.Collections.Generic;
using NRediSearch.Aggregation.Reducers;
using StackExchange.Redis;

namespace NRediSearch.Aggregation
{
    public class AggregationRequest
    {
        private readonly string _query;
        private readonly List<string> _load = new List<string>();
        private readonly List<Group> _groups = new List<Group>();
        private readonly List<SortedField> _sortby = new List<SortedField>();
        private readonly Dictionary<string, string> _projections = new Dictionary<string, string>();

        private Limit _limit = new Limit(0, 0);
        private int _sortByMax = 0;
        public AggregationRequest(string query)
        {
            _query = query;
        }
        public AggregationRequest() : this("*") { }

        public AggregationRequest Load(string field)
        {
            _load.Add(field);
            return this;
        }
        public AggregationRequest Load(params string[] fields)
        {
            _load.AddRange(fields);
            return this;
        }

        public AggregationRequest Limit(int offset, int count)
        {
            var limit = new Limit(offset, count);
            if (_groups.Count == 0)
            {
                _limit = limit;
            }
            else
            {
                _groups[_groups.Count - 1].Limit(limit);
            }
            return this;
        }

        public AggregationRequest Limit(int count) => Limit(0, count);

        public AggregationRequest SortBy(SortedField field)
        {
            _sortby.Add(field);
            return this;
        }
        public AggregationRequest SortBy(params SortedField[] fields)
        {
            _sortby.AddRange(fields);
            return this;
        }
        public AggregationRequest SortBy(IList<SortedField> fields, int max)
        {
            _sortby.AddRange(fields);
            _sortByMax = max;
            return this;
        }
        public AggregationRequest SortBy(SortedField field, int max)
        {
            _sortby.Add(field);
            _sortByMax = max;
            return this;
        }

        public AggregationRequest SortBy(string field, Order order) => SortBy(new SortedField(field, order));
        public AggregationRequest SortByAscending(string field) => SortBy(field, Order.Ascending);
        public AggregationRequest SortByDescending(string field) => SortBy(field, Order.Descending);

        public AggregationRequest Apply(string projection, string alias)
        {
            _projections.Add(alias, projection);
            return this;
        }

        public AggregationRequest GroupBy(IList<string> fields, IList<Reducer> reducers)
        {
            Group g = new Group(fields);
            foreach (var r in reducers)
            {
                g.Reduce(r);
            }
            _groups.Add(g);
            return this;
        }

        public AggregationRequest GroupBy(String field, params Reducer[] reducers)
        {
            return GroupBy(new string[] { field }, reducers);
        }

        public AggregationRequest GroupBy(Group group)
        {
            _groups.Add(group);
            return this;
        }

        private static void AddCmdLen(List<object> list, string cmd, int len)
        {
            list.Add(cmd.Literal());
            list.Add(len);
        }
        private static void AddCmdArgs<T>(List<object> dst, string cmd, IList<T> src)
        {
            AddCmdLen(dst, cmd, src.Count);
            foreach (var obj in src)
                dst.Add(obj);
        }

        internal void SerializeRedisArgs(List<object> args)
        {
            args.Add(_query);

            if (_load.Count != 0)
            {
                AddCmdArgs(args, "LOAD", _load);
            }

            if (_groups.Count != 0)
            {
                foreach (var group in _groups)
                {
                    args.Add("GROUPBY".Literal());
                    group.SerializeRedisArgs(args);
                }
            }

            if (_projections.Count != 0)
            {
                args.Add("APPLY".Literal());
                foreach (var e in _projections)
                {
                    args.Add(e.Value);
                    args.Add("AS".Literal());
                    args.Add(e.Key);
                }
            }

            if (_sortby.Count != 0)
            {
                args.Add("SORTBY".Literal());
                args.Add((_sortby.Count * 2).Boxed());
                foreach (var field in _sortby)
                {
                    args.Add(field.Field);
                    args.Add(field.OrderAsArg());
                }
                if (_sortByMax > 0)
                {
                    args.Add("MAX".Literal());
                    args.Add(_sortByMax.Boxed());
                }
            }

            _limit.SerializeRedisArgs(args);
        }
    }
}
