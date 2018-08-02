// .NET port of https://github.com/RedisLabs/JRediSearch/

using System;

namespace NRediSearch.QueryBuilder
{
    public static class Values
    {
        private abstract class ScalableValue : Value
        {
            public override bool IsCombinable() => true;
        }
        private sealed class ValueValue : ScalableValue
        {
            private readonly string s;
            public ValueValue(string s)
            {
                this.s = s;
            }
            public override string ToString() => s;
        }
        public static Value Value(string s) => new ValueValue(s);

        internal static Value[] Value(string[] s) => Array.ConvertAll(s, _ => Value(_));

        public static RangeValue Between(double from, double to) => new RangeValue(from, to);

        public static RangeValue Between(int from, int to) => new RangeValue((double)from, (double)to);

        public static RangeValue Equal(double d) => new RangeValue(d, d);

        public static RangeValue Equal(int i) => Equal((double)i);

        public static RangeValue LessThan(double d) => new RangeValue(double.NegativeInfinity, d).InclusiveMax(false);

        public static RangeValue GreaterThan(double d) => new RangeValue(d, double.PositiveInfinity).InclusiveMin(false);
        public static RangeValue LessThanOrEqual(double d) => LessThan(d).InclusiveMax(true);

        public static RangeValue GreaterThanOrEqual(double d) => GreaterThan(d).InclusiveMin(true);

        public static Value Tags(params string[] tags)
        {
            if (tags.Length == 0)
            {
                throw new ArgumentException("Must have at least one tag", nameof(tags));
            }
            return new TagValue("{" + string.Join(" | ", tags) + "}");
        }
        private sealed class TagValue : Value
        {
            private readonly string s;
            public TagValue(string s) { this.s = s; }
            public override string ToString() => s;
        }
    }
}
