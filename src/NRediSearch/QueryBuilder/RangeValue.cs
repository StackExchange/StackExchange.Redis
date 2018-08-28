// .NET port of https://github.com/RedisLabs/JRediSearch/

using System.Text;

namespace NRediSearch.QueryBuilder
{
    public sealed class RangeValue : Value
    {
        private readonly double from, to;
        private bool inclusiveMin = true, inclusiveMax = true;

        public override bool IsCombinable() => false;

        private static void AppendNum(StringBuilder sb, double n, bool inclusive)
        {
            if (!inclusive)
            {
                sb.Append("(");
            }
            sb.Append(n.AsRedisString(true));
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("[");
            AppendNum(sb, from, inclusiveMin);
            sb.Append(" ");
            AppendNum(sb, to, inclusiveMax);
            sb.Append("]");
            return sb.ToString();
        }

        public RangeValue(double from, double to)
        {
            this.from = from;
            this.to = to;
        }

        public RangeValue InclusiveMin(bool val)
        {
            inclusiveMin = val;
            return this;
        }
        public RangeValue InclusiveMax(bool val)
        {
            inclusiveMax = val;
            return this;
        }
    }
}
