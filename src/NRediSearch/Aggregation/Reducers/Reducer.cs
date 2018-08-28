// .NET port of https://github.com/RedisLabs/JRediSearch/

using System;
using System.Collections.Generic;

namespace NRediSearch.Aggregation.Reducers
{
    // This class is normally received via one of the subclasses or via Reducers
    public abstract class Reducer
    {
        public override string ToString() => Name;
        private readonly string _field;

        internal Reducer(string field) => _field = field;

        /// <summary>
        /// The name of the reducer
        /// </summary>
        public abstract string Name { get; }

        public string Alias { get; set; }

        public Reducer As(string alias)
        {
            Alias = alias;
            return this;
        }
        public Reducer SetAliasAsField()
        {
            if (string.IsNullOrEmpty(_field)) throw new InvalidOperationException("Cannot set to field name since no field exists");
            return As(_field);
        }

        protected virtual int GetOwnArgsCount() => _field == null ? 0 : 1;
        protected virtual void AddOwnArgs(List<object> args)
        {
            if (_field != null) args.Add(_field);
        }

        internal void SerializeRedisArgs(List<object> args)
        {
            int count = GetOwnArgsCount();
            args.Add(count.Boxed());
            int before = args.Count;
            AddOwnArgs(args);
            int after = args.Count;
            if (count != (after - before))
                throw new InvalidOperationException($"Reducer '{ToString()}' incorrectly reported the arg-count as {count}, but added {after - before}");
        }
    }
}
