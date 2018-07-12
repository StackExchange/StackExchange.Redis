using System.Text;

namespace NRediSearch.QueryBuilder
{
    internal ref struct StringJoiner
    {
        readonly StringBuilder _sb;
        readonly string _delimiter;
        bool _isFirst;
        public StringJoiner(StringBuilder sb, string delimiter)
        {
            _sb = sb;
            _delimiter = delimiter;
            _isFirst = true;
        }
        public void Add(string value)
        {
            if (_isFirst) _isFirst = false;
            else _sb.Append(_delimiter);
            _sb.Append(value);
        }
    }
}
