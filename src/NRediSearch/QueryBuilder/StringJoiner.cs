// .NET port of https://github.com/RedisLabs/JRediSearch/

using System.Text;

namespace NRediSearch.QueryBuilder
{
    internal ref struct StringJoiner // this is to replace a Java feature cleanly
    {
        private readonly StringBuilder _sb;
        private readonly string _delimiter;
        private bool _isFirst;
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
