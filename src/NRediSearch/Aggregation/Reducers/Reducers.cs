// .NET port of https://github.com/RedisLabs/JRediSearch/

using System.Collections.Generic;

namespace NRediSearch.Aggregation.Reducers
{
    public static class Reducers
    {
        public static Reducer Count() => CountReducer.Instance;
        private sealed class CountReducer : Reducer
        {
            internal static readonly Reducer Instance = new CountReducer();
            private CountReducer() : base(null) { }
            public override string Name => "COUNT";
        }

        private sealed class SingleFieldReducer : Reducer
        {
            public override string Name { get; }

            internal SingleFieldReducer(string name, string field) : base(field)
            {
                Name = name;
            }
        }

        public static Reducer CountDistinct(string field) => new SingleFieldReducer("COUNT_DISTINCT", field);

        public static Reducer CountDistinctish(string field) => new SingleFieldReducer("COUNT_DISTINCTISH", field);

        public static Reducer Sum(string field) => new SingleFieldReducer("SUM", field);

        public static Reducer Min(string field) => new SingleFieldReducer("MIN", field);

        public static Reducer Max(string field) => new SingleFieldReducer("MAX", field);

        public static Reducer Avg(string field) => new SingleFieldReducer("AVG", field);

        public static Reducer StdDev(string field) => new SingleFieldReducer("STDDEV", field);

        public static Reducer Quantile(string field, double percentile) => new QuantileReducer(field, percentile);

        private sealed class QuantileReducer : Reducer
        {
            private readonly double _percentile;
            public QuantileReducer(string field, double percentile) : base(field)
            {
                _percentile = percentile;
            }
            protected override int GetOwnArgsCount() => base.GetOwnArgsCount() + 1;
            protected override void AddOwnArgs(List<object> args)
            {
                base.AddOwnArgs(args);
                args.Add(_percentile);
            }
            public override string Name => "QUANTILE";
        }
        public static Reducer FirstValue(string field, SortedField sortBy) => new FirstValueReducer(field, sortBy);
        private sealed class FirstValueReducer : Reducer
        {
            private readonly SortedField? _sortBy;
            public FirstValueReducer(string field, SortedField? sortBy) : base(field)
            {
                _sortBy = sortBy;
            }
            public override string Name => "FIRST_VALUE";

            protected override int GetOwnArgsCount() => base.GetOwnArgsCount() + (_sortBy.HasValue ? 3 : 0);
            protected override void AddOwnArgs(List<object> args)
            {
                base.AddOwnArgs(args);
                if (_sortBy != null)
                {
                    var sortBy = _sortBy.GetValueOrDefault();
                    args.Add("BY".Literal());
                    args.Add(sortBy.Field);
                    args.Add(sortBy.OrderAsArg());
                }
            }
        }
        public static Reducer FirstValue(string field) => new FirstValueReducer(field, null);

        public static Reducer ToList(string field) => new SingleFieldReducer("TOLIST", field);

        public static Reducer RandomSample(string field, int size) => new RandomSampleReducer(field, size);

        private sealed class RandomSampleReducer : Reducer
        {
            private readonly int _size;
            public RandomSampleReducer(string field, int size) : base(field)
            {
                _size = size;
            }
            public override string Name => "RANDOM_SAMPLE";
            protected override int GetOwnArgsCount() => base.GetOwnArgsCount() + 1;
            protected override void AddOwnArgs(List<object> args)
            {
                base.AddOwnArgs(args);
                args.Add(_size.Boxed());
            }
        }
    }
}
