// .NET port of https://github.com/RedisLabs/JRediSearch/

using System.Collections.Generic;
using NRediSearch.Aggregation.Reducers;

namespace NRediSearch.Aggregation
{
    public sealed class Group
    {
        private readonly IList<Reducer> _reducers = new List<Reducer>();
        private readonly IList<string> _fields;
        private Limit _limit = new Limit(0, 0);

        public Group(params string[] fields) => _fields = fields;

        public Group(IList<string> fields) => _fields = fields;

        internal Group Limit(Limit limit)
        {
            _limit = limit;
            return this;
        }

        internal Group Reduce(Reducer r)
        {
            _reducers.Add(r);
            return this;
        }

        internal void SerializeRedisArgs(List<object> args)
        {
            args.Add(_fields.Count.Boxed());
            foreach (var field in _fields)
                args.Add(field);
            foreach (var r in _reducers)
            {
                args.Add("REDUCE".Literal());
                args.Add(r.Name.Literal());
                r.SerializeRedisArgs(args);
                var alias = r.Alias;
                if (!string.IsNullOrEmpty(alias))
                {
                    args.Add("AS".Literal());
                    args.Add(alias);
                }
            }
            _limit.SerializeRedisArgs(args);
        }
    }
}
