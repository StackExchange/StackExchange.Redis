// .NET port of https://github.com/RedisLabs/JRediSearch/

using System.Text;

namespace NRediSearch.QueryBuilder
{
    public class ValueNode : INode
    {
        private readonly Value[] _values;
        private readonly string _field, _joinString;

        public ValueNode(string field, string joinstr, params Value[] values)
        {
            _field = field;
            _values = values;
            _joinString = joinstr;
        }

        private static Value[] FromStrings(string[] values)
        {
            Value[] objs = new Value[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                objs[i] = Values.Value(values[i]);
            }
            return objs;
        }

        public ValueNode(string field, string joinstr, params string[] values)
            : this(field, joinstr, FromStrings(values)) { }

        private string FormatField()
        {
            if (string.IsNullOrWhiteSpace(_field)) return "";
            return "@" + _field + ":";
        }

        private string ToStringCombinable(ParenMode mode)
        {
            StringBuilder sb = new StringBuilder(FormatField());
            if (_values.Length > 1 || mode == ParenMode.Always)
            {
                sb.Append("(");
            }
            var sj = new StringJoiner(sb, _joinString);
            foreach (var v in _values)
            {
                sj.Add(v.ToString());
            }
            if (_values.Length > 1 || mode == ParenMode.Always)
            {
                sb.Append(")");
            }
            return sb.ToString();
        }

        private string ToStringDefault(ParenMode mode)
        {
            bool useParen = mode == ParenMode.Always;
            if (!useParen)
            {
                useParen = mode != ParenMode.Never && _values.Length > 1;
            }
            var sb = new StringBuilder();
            if (useParen)
            {
                sb.Append("(");
            }
            var sj = new StringJoiner(sb, _joinString);
            foreach (var v in _values)
            {
                sj.Add(FormatField() + v);
            }
            if (useParen)
            {
                sb.Append(")");
            }
            return sb.ToString();
        }

        public string ToString(ParenMode mode)
        {
            if (_values[0].IsCombinable())
            {
                return ToStringCombinable(mode);
            }
            else
            {
                return ToStringDefault(mode);
            }
        }
    }
}
